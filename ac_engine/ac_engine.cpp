#include "ac_engine.h"
#include <atomic>
#include <thread>
#include <mutex>
#include <deque>
#include <chrono>
#include <algorithm>
#include <cmath>

// Defined in dllmain.cpp. Used so low-level hooks use this DLL's module handle.
extern HMODULE g_acEngineModule;

static const ULONG_PTR INJECTED_SIGNATURE = 0xACAC1234;

struct EngineState {
    std::mutex settingsMutex;
    ClickerSettings settings;
    std::atomic<bool> enabled{false};
    std::atomic<bool> requestExit{false};
    std::atomic<bool> realMouseDown{false};
    std::atomic<long long> lastRealMouseDownTick{0};
    std::atomic<long long> lastRealMouseUpTick{0};
    std::mutex clickTimestampsMutex;
    std::deque<long long> realClickTimestampsMs;
    HHOOK mouseHook = nullptr;
    HHOOK keyboardHook = nullptr;
    std::atomic<RunState> runState{RunState::Idle};
    std::atomic<long long> stateEnteredTickMs{0};
    HWND hMainWnd = nullptr;
    std::thread clickerThread;
    std::thread hookThread;
    HANDLE hookThreadReadyEvent = nullptr;
    DWORD hookThreadId = 0;
};

static EngineState g;

static long long NowMs() {
    return std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
}

static void PostStatusUpdate(int code) {
    if (g.hMainWnd) {
        PostMessage(g.hMainWnd, AC_WM_STATUS_UPDATE, (WPARAM)code, 0);
    }
}

static void RecordRealClickTimestamp() {
    long long now = NowMs();
    std::lock_guard<std::mutex> lock(g.clickTimestampsMutex);
    g.realClickTimestampsMs.push_back(now);
    long long cutoff = now - 2000;
    while (!g.realClickTimestampsMs.empty() && g.realClickTimestampsMs.front() < cutoff) {
        g.realClickTimestampsMs.pop_front();
    }
}

static double ComputeRecentCPS(int windowMs) {
    long long now = NowMs();
    long long cutoff = now - windowMs;
    std::lock_guard<std::mutex> lock(g.clickTimestampsMutex);
    int count = 0;
    for (auto it = g.realClickTimestampsMs.rbegin(); it != g.realClickTimestampsMs.rend(); ++it) {
        if (*it < cutoff) break;
        count++;
    }
    if (windowMs <= 0) return 0.0;
    return (double)count / ((double)windowMs / 1000.0);
}

static bool IsSyntheticEvent(const MSLLHOOKSTRUCT* info) {
    return info->dwExtraInfo == INJECTED_SIGNATURE;
}

static LRESULT CALLBACK LowLevelMouseProc(int nCode, WPARAM wParam, LPARAM lParam) {
    if (nCode == HC_ACTION) {
        MSLLHOOKSTRUCT* info = (MSLLHOOKSTRUCT*)lParam;
        bool synthetic = IsSyntheticEvent(info);
        if (wParam == WM_LBUTTONDOWN) {
            if (!synthetic) {
                g.realMouseDown.store(true);
                g.lastRealMouseDownTick.store(NowMs());
                RecordRealClickTimestamp();
            }
        } else if (wParam == WM_LBUTTONUP) {
            if (!synthetic) {
                g.realMouseDown.store(false);
                g.lastRealMouseUpTick.store(NowMs());
            }
        }
    }
    return CallNextHookEx(g.mouseHook, nCode, wParam, lParam);
}

static LRESULT CALLBACK LowLevelKeyboardProc(int nCode, WPARAM wParam, LPARAM lParam) {
    return CallNextHookEx(g.keyboardHook, nCode, wParam, lParam);
}

static bool IsRealMouseCurrentlyDown() {
    // Trust the low-level hook's tracked state. The hook ignores our synthetic
    // clicks (they carry INJECTED_SIGNATURE), so g.realMouseDown is only
    // updated by genuine user input. GetAsyncKeyState alone is unreliable here
    // because our own SendInput bursts can make it transiently report "up"
    // even while the user is physically holding the button down.
    return g.realMouseDown.load();
}

static void SendSyntheticClick() {
    INPUT inputs[2] = {};
    inputs[0].type = INPUT_MOUSE;
    inputs[0].mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
    inputs[0].mi.dwExtraInfo = INJECTED_SIGNATURE;
    inputs[1].type = INPUT_MOUSE;
    inputs[1].mi.dwFlags = MOUSEEVENTF_LEFTUP;
    inputs[1].mi.dwExtraInfo = INJECTED_SIGNATURE;
    SendInput(2, inputs, sizeof(INPUT));
}

static DWORD WINAPI HookThreadProc(LPVOID) {
    g.hookThreadId = GetCurrentThreadId();
    HINSTANCE hInst = g_acEngineModule ? g_acEngineModule : GetModuleHandle(nullptr);
    g.mouseHook = SetWindowsHookExA(WH_MOUSE_LL, LowLevelMouseProc, hInst, 0);
    g.keyboardHook = SetWindowsHookExA(WH_KEYBOARD_LL, LowLevelKeyboardProc, hInst, 0);
    SetEvent(g.hookThreadReadyEvent);
    MSG msg;
    while (!g.requestExit.load()) {
        BOOL result = GetMessage(&msg, nullptr, 0, 0);
        if (result <= 0) break;
        TranslateMessage(&msg);
        DispatchMessage(&msg);
    }
    if (g.mouseHook) { UnhookWindowsHookEx(g.mouseHook); g.mouseHook = nullptr; }
    if (g.keyboardHook) { UnhookWindowsHookEx(g.keyboardHook); g.keyboardHook = nullptr; }
    return 0;
}

static void StartHookThread() {
    g.hookThreadReadyEvent = CreateEventA(nullptr, TRUE, FALSE, nullptr);
    g.hookThread = std::thread([]() { HookThreadProc(nullptr); });
    WaitForSingleObject(g.hookThreadReadyEvent, 5000);
}

static void StopHookThread() {
    if (g.hookThreadId != 0) {
        PostThreadMessage(g.hookThreadId, WM_QUIT, 0, 0);
    }
    if (g.hookThread.joinable()) g.hookThread.join();
    if (g.hookThreadReadyEvent) { CloseHandle(g.hookThreadReadyEvent); g.hookThreadReadyEvent = nullptr; }
}

static ClickerSettings SnapshotSettings() {
    std::lock_guard<std::mutex> lock(g.settingsMutex);
    return g.settings;
}

struct ReleaseSampleHistory {
    std::deque<std::pair<long long, bool>> samples;
};

static ReleaseSampleHistory g_spamReleaseHistory;
static ReleaseSampleHistory g_holdReleaseHistory;

static void ResetReleaseHistory(ReleaseSampleHistory& h) {
    h.samples.clear();
}

static bool SampleAndCheckReleased(ReleaseSampleHistory& history, bool doingHold, const ClickerSettings& s) {
    bool stillActive;
    if (doingHold) {
        stillActive = IsRealMouseCurrentlyDown();
    } else {
        // Spam mode: the user is still considered active if they have produced
        // a real (non-synthetic) click within the last trigger-sample window.
        // Using a single, consistent window (the same one used to *start*)
        // avoids start/stop flapping near the threshold CPS. The CPS rate check
        // still applies so holding the button without clicking does NOT keep it
        // running (matching the "trigger" concept).
        double cps = ComputeRecentCPS(s.triggerSampleWindowMs);
        stillActive = s.spamTriggerCPS > 0.0 && cps >= s.spamTriggerCPS;
    }
    long long now = NowMs();
    history.samples.push_back({now, !stillActive});
    int windowMs = s.stopCheckWindowMs > 0 ? s.stopCheckWindowMs : 150;
    long long cutoff = now - windowMs;
    while (!history.samples.empty() && history.samples.front().first < cutoff) {
        history.samples.pop_front();
    }
    int requiredChecks = s.stopCheckCount > 0 ? s.stopCheckCount : 3;
    if ((int)history.samples.size() < requiredChecks) return false;
    for (auto it = history.samples.rbegin(); it != history.samples.rend() && requiredChecks > 0; ++it, --requiredChecks) {
        if (!it->second) return false;
    }
    return true;
}

static void RunSpamModeLoop() {
    long long lastClickTime = 0;
    while (g.enabled.load() && !g.requestExit.load()) {
        ClickerSettings s = SnapshotSettings();
        if (s.mode != ClickMode::Spam) return;
        RunState state = g.runState.load();
        if (state == RunState::Idle) {
            double cps = ComputeRecentCPS(s.triggerSampleWindowMs);
            if (s.spamTriggerCPS > 0.0 && cps >= s.spamTriggerCPS) {
                g.runState.store(RunState::ArmedWaitingDelay);
                g.stateEnteredTickMs.store(NowMs());
                PostStatusUpdate(1);
            }
        } else if (state == RunState::ArmedWaitingDelay) {
            long long elapsed = NowMs() - g.stateEnteredTickMs.load();
            double cps = ComputeRecentCPS(s.triggerSampleWindowMs);
            if (cps < s.spamTriggerCPS) {
                g.runState.store(RunState::Idle);
                PostStatusUpdate(0);
            } else if (elapsed >= s.spamDelayMs) {
                g.runState.store(RunState::Active);
                lastClickTime = 0;
                ResetReleaseHistory(g_spamReleaseHistory);
                PostStatusUpdate(2);
            }
        } else if (state == RunState::Active) {
            if (s.spamAutoClickCPS > 0.0) {
                long long intervalMs = (long long)std::llround(1000.0 / s.spamAutoClickCPS);
                if (intervalMs < s.minClickIntervalMs) intervalMs = s.minClickIntervalMs;
                long long now = NowMs();
                if (now - lastClickTime >= intervalMs) {
                    SendSyntheticClick();
                    lastClickTime = now;
                }
            }
            bool released = SampleAndCheckReleased(g_spamReleaseHistory, false, s);
            if (released) {
                g.runState.store(RunState::Idle);
                PostStatusUpdate(0);
            }
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(s.clickerThreadTickMs));
    }
}

static bool WaitForKeyIsDown(UINT vk) {
    if (vk == 0) return true;
    SHORT s = GetAsyncKeyState((int)vk);
    return (s & 0x8000) != 0;
}

static void RunHoldModeLoop() {
    long long lastClickTime = 0;
    bool prevMouseDown = false;
    long long firstClickDownTime = 0;
    bool waitingSecondArmed = false;
    while (g.enabled.load() && !g.requestExit.load()) {
        ClickerSettings s = SnapshotSettings();
        if (s.mode != ClickMode::Hold) return;
        bool mouseDownNow = IsRealMouseCurrentlyDown();
        RunState state = g.runState.load();
        if (state == RunState::Idle) {
            if (s.holdSubMode == HoldSubMode::Immediate) {
                if (mouseDownNow && !prevMouseDown) {
                    g.runState.store(RunState::ArmedWaitingDelay);
                    g.stateEnteredTickMs.store(NowMs());
                    PostStatusUpdate(1);
                }
            } else if (s.holdSubMode == HoldSubMode::WaitForKey) {
                if (mouseDownNow && !prevMouseDown && WaitForKeyIsDown(s.waitForKeyVK)) {
                    g.runState.store(RunState::ArmedWaitingDelay);
                    g.stateEnteredTickMs.store(NowMs());
                    PostStatusUpdate(1);
                }
            } else if (s.holdSubMode == HoldSubMode::DoubleClick) {
                if (mouseDownNow && !prevMouseDown) {
                    if (!waitingSecondArmed) {
                        firstClickDownTime = NowMs();
                        waitingSecondArmed = false;
                    }
                }
                if (!mouseDownNow && prevMouseDown && firstClickDownTime != 0 && !waitingSecondArmed) {
                    waitingSecondArmed = true;
                    g.runState.store(RunState::WaitingSecondClick);
                    g.stateEnteredTickMs.store(NowMs());
                    PostStatusUpdate(3);
                }
            }
        } else if (state == RunState::WaitingSecondClick) {
            long long elapsedSinceFirstDown = NowMs() - firstClickDownTime;
            if (elapsedSinceFirstDown > s.doubleClickIntervalMs && !mouseDownNow) {
                g.runState.store(RunState::Idle);
                waitingSecondArmed = false;
                firstClickDownTime = 0;
                PostStatusUpdate(0);
            } else if (mouseDownNow && !prevMouseDown) {
                long long secondDownTime = NowMs();
                if (secondDownTime - firstClickDownTime <= s.doubleClickIntervalMs) {
                    g.runState.store(RunState::ArmedWaitingDelay);
                    g.stateEnteredTickMs.store(NowMs());
                    waitingSecondArmed = false;
                    firstClickDownTime = 0;
                    PostStatusUpdate(1);
                } else {
                    g.runState.store(RunState::Idle);
                    waitingSecondArmed = false;
                    firstClickDownTime = 0;
                    PostStatusUpdate(0);
                }
            }
        } else if (state == RunState::ArmedWaitingDelay) {
            long long elapsed = NowMs() - g.stateEnteredTickMs.load();
            bool keyStillOk = (s.holdSubMode != HoldSubMode::WaitForKey) || WaitForKeyIsDown(s.waitForKeyVK);
            if (!mouseDownNow || !keyStillOk) {
                g.runState.store(RunState::Idle);
                PostStatusUpdate(0);
            } else if (elapsed >= s.holdDelayMs) {
                g.runState.store(RunState::Active);
                lastClickTime = 0;
                ResetReleaseHistory(g_holdReleaseHistory);
                PostStatusUpdate(2);
            }
        } else if (state == RunState::Active) {
            if (s.holdAutoClickCPS > 0.0) {
                long long intervalMs = (long long)std::llround(1000.0 / s.holdAutoClickCPS);
                if (intervalMs < s.minClickIntervalMs) intervalMs = s.minClickIntervalMs;
                long long now = NowMs();
                if (now - lastClickTime >= intervalMs) {
                    SendSyntheticClick();
                    lastClickTime = now;
                }
            }
            bool released = SampleAndCheckReleased(g_holdReleaseHistory, true, s);
            if (released) {
                g.runState.store(RunState::Idle);
                PostStatusUpdate(0);
            }
        }
        prevMouseDown = mouseDownNow;
        std::this_thread::sleep_for(std::chrono::milliseconds(s.clickerThreadTickMs));
    }
}

static void ClickerThreadMain() {
    while (!g.requestExit.load()) {
        if (!g.enabled.load()) {
            std::this_thread::sleep_for(std::chrono::milliseconds(15));
            continue;
        }
        g.runState.store(RunState::Idle);
        ClickerSettings s = SnapshotSettings();
        if (s.mode == ClickMode::Spam) {
            RunSpamModeLoop();
        } else {
            RunHoldModeLoop();
        }
    }
}

extern "C" AC_API
BOOL AC_Init(HWND hMainWnd) {
    g.hMainWnd = hMainWnd;
    g.requestExit.store(false);
    g.enabled.store(false);
    StartHookThread();
    g.clickerThread = std::thread(ClickerThreadMain);
    return TRUE;
}

extern "C" AC_API
void AC_Shutdown() {
    g.requestExit.store(true);
    g.enabled.store(false);
    if (g.clickerThread.joinable()) g.clickerThread.join();
    StopHookThread();
}

extern "C" AC_API
void AC_SetSettings(const ClickerSettings* s) {
    if (!s) return;
    std::lock_guard<std::mutex> lock(g.settingsMutex);
    g.settings = *s;
}

extern "C" AC_API
void AC_Enable() {
    g.enabled.store(true);
}

extern "C" AC_API
void AC_Disable() {
    g.enabled.store(false);
    g.runState.store(RunState::Idle);
}

extern "C" AC_API
BOOL AC_IsEnabled() {
    return g.enabled.load() ? TRUE : FALSE;
}

extern "C" AC_API
int AC_GetRunState() {
    return (int)g.runState.load();
}
