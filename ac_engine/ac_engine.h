#pragma once
#include <windows.h>

enum class ClickMode { Spam = 0, Hold = 1 };
enum class HoldSubMode { Immediate = 0, DoubleClick = 1, WaitForKey = 2 };

enum class RunState {
    Idle = 0,
    ArmedWaitingDelay = 1,
    WaitingSecondClick = 2,
    Active = 3
};

struct ClickerSettings {
    ClickMode mode = ClickMode::Spam;
    double spamAutoClickCPS = 10.0;
    double spamTriggerCPS = 5.0;
    int spamDelayMs = 100;
    double holdAutoClickCPS = 10.0;
    int holdDelayMs = 100;
    HoldSubMode holdSubMode = HoldSubMode::Immediate;
    int doubleClickIntervalMs = 300;
    UINT waitForKeyVK = VK_SHIFT;
    int stopCheckCount = 3;
    int stopCheckWindowMs = 150;
    int triggerSampleWindowMs = 400;
    int clickerThreadTickMs = 2;
    int minClickIntervalMs = 1;
};

#define AC_WM_STATUS_UPDATE (WM_APP + 1)

#ifdef AC_BUILD_DLL
#define AC_API __declspec(dllexport)
#else
#define AC_API
#endif

extern "C" {
AC_API BOOL AC_Init(HWND hMainWnd);
AC_API void AC_Shutdown();
AC_API void AC_SetSettings(const ClickerSettings* s);
AC_API void AC_Enable();
AC_API void AC_Disable();
AC_API BOOL AC_IsEnabled();
AC_API int AC_GetRunState();
}

