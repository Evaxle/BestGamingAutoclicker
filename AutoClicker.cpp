#define WIN32_LEAN_AND_MEAN
#define _WIN32_WINNT 0x0601
#include <windows.h>
#include <commctrl.h>
#include <shlobj.h>
#include <string>
#include <vector>
#include <map>
#include <fstream>
#include <sstream>
#include <atomic>
#include <mutex>
#include <algorithm>
#include <cmath>

#include "ac_engine.h"

// Runtime bindings to ac_engine.dll exports (loaded via LoadLibrary at startup).
typedef BOOL (*AC_InitFn)(HWND);
typedef void (*AC_ShutdownFn)();
typedef void (*AC_SetSettingsFn)(const ClickerSettings*);
typedef void (*AC_EnableFn)();
typedef void (*AC_DisableFn)();
typedef BOOL (*AC_IsEnabledFn)();
typedef int (*AC_GetRunStateFn)();

struct AppState {
    std::mutex settingsMutex;
    ClickerSettings settings;

    std::atomic<bool> unsavedChanges{false};

    std::string exeDir;
    std::string dataDir;

    HMODULE hEngine = nullptr;
    AC_InitFn AC_Init = nullptr;
    AC_ShutdownFn AC_Shutdown = nullptr;
    AC_SetSettingsFn AC_SetSettings = nullptr;
    AC_EnableFn AC_Enable = nullptr;
    AC_DisableFn AC_Disable = nullptr;
    AC_IsEnabledFn AC_IsEnabled = nullptr;
    AC_GetRunStateFn AC_GetRunState = nullptr;
};

static AppState g_app;

static std::string TrimCopy(const std::string& s) {
    size_t a = s.find_first_not_of(" \t\r\n");
    if (a == std::string::npos) return "";
    size_t b = s.find_last_not_of(" \t\r\n");
    return s.substr(a, b - a + 1);
}

class MiniJsonWriter {
public:
    void Begin() { out << "{\n"; first = true; }
    void End() { out << "\n}\n"; }
    void KV(const std::string& key, const std::string& val) {
        Comma();
        out << "  \"" << Escape(key) << "\": \"" << Escape(val) << "\"";
    }
    void KV(const std::string& key, double val) {
        Comma();
        out << "  \"" << Escape(key) << "\": " << val;
    }
    void KV(const std::string& key, int val) {
        Comma();
        out << "  \"" << Escape(key) << "\": " << val;
    }
    void KVBool(const std::string& key, bool val) {
        Comma();
        out << "  \"" << Escape(key) << "\": " << (val ? "true" : "false");
    }
    std::string Str() const { return out.str(); }

private:
    std::ostringstream out;
    bool first = true;
    void Comma() {
        if (!first) out << ",\n";
        first = false;
    }
    static std::string Escape(const std::string& s) {
        std::string r;
        r.reserve(s.size());
        for (char c : s) {
            if (c == '\\' || c == '"') r += '\\';
            r += c;
        }
        return r;
    }
};

class MiniJsonReader {
public:
    std::map<std::string, std::string> values;

    bool Parse(const std::string& text) {
        values.clear();
        size_t i = 0;
        size_t n = text.size();
        SkipWs(text, i);
        if (i >= n || text[i] != '{') return false;
        i++;
        while (true) {
            SkipWs(text, i);
            if (i >= n) return false;
            if (text[i] == '}') { i++; break; }
            std::string key;
            if (!ParseString(text, i, key)) return false;
            SkipWs(text, i);
            if (i >= n || text[i] != ':') return false;
            i++;
            SkipWs(text, i);
            std::string val;
            if (!ParseValue(text, i, val)) return false;
            values[key] = val;
            SkipWs(text, i);
            if (i < n && text[i] == ',') { i++; continue; }
            if (i < n && text[i] == '}') { i++; break; }
            return false;
        }
        return true;
    }

    std::string GetStr(const std::string& key, const std::string& def) const {
        auto it = values.find(key);
        return it == values.end() ? def : it->second;
    }
    double GetDouble(const std::string& key, double def) const {
        auto it = values.find(key);
        if (it == values.end()) return def;
        try { return std::stod(it->second); } catch (...) { return def; }
    }
    int GetInt(const std::string& key, int def) const {
        auto it = values.find(key);
        if (it == values.end()) return def;
        try { return std::stoi(it->second); } catch (...) { return def; }
    }
    bool GetBool(const std::string& key, bool def) const {
        auto it = values.find(key);
        if (it == values.end()) return def;
        return it->second == "true";
    }

private:
    static void SkipWs(const std::string& t, size_t& i) {
        while (i < t.size() && (t[i] == ' ' || t[i] == '\t' || t[i] == '\r' || t[i] == '\n')) i++;
    }
    static bool ParseString(const std::string& t, size_t& i, std::string& out) {
        if (i >= t.size() || t[i] != '"') return false;
        i++;
        out.clear();
        while (i < t.size() && t[i] != '"') {
            if (t[i] == '\\' && i + 1 < t.size()) {
                i++;
                out += t[i];
            } else {
                out += t[i];
            }
            i++;
        }
        if (i >= t.size()) return false;
        i++;
        return true;
    }
    static bool ParseValue(const std::string& t, size_t& i, std::string& out) {
        if (i >= t.size()) return false;
        if (t[i] == '"') return ParseString(t, i, out);
        if (t.compare(i, 4, "true") == 0) { out = "true"; i += 4; return true; }
        if (t.compare(i, 5, "false") == 0) { out = "false"; i += 5; return true; }
        if (t.compare(i, 4, "null") == 0) { out = ""; i += 4; return true; }
        size_t start = i;
        while (i < t.size() && (isdigit((unsigned char)t[i]) || t[i] == '-' || t[i] == '+' ||
                                 t[i] == '.' || t[i] == 'e' || t[i] == 'E')) i++;
        if (i == start) return false;
        out = t.substr(start, i - start);
        return true;
    }
};

#define ID_RADIO_SPAM 1001
#define ID_RADIO_HOLD 1002

#define ID_EDIT_SPAM_AUTOCPS 1010
#define ID_EDIT_SPAM_TRIGGERCPS 1011
#define ID_EDIT_SPAM_DELAY 1012

#define ID_EDIT_HOLD_AUTOCPS 1020
#define ID_EDIT_HOLD_DELAY 1021
#define ID_RADIO_HOLD_IMMEDIATE 1022
#define ID_RADIO_HOLD_DOUBLECLICK 1023
#define ID_RADIO_HOLD_WAITKEY 1024
#define ID_EDIT_DOUBLECLICK_INTERVAL 1025
#define ID_BUTTON_KEYSELECT 1026

#define ID_COMBO_PROFILE 1030
#define ID_BUTTON_NEWPROFILE 1031
#define ID_BUTTON_DELETEPROFILE 1032
#define ID_BUTTON_SAVEPROFILE 1033

#define ID_BUTTON_ENABLE 1040

#define ID_BUTTON_ADVANCED_TOGGLE 1050
#define ID_EDIT_ADV_STOPCHECKCOUNT 1051
#define ID_EDIT_ADV_STOPCHECKWINDOW 1052
#define ID_EDIT_ADV_TRIGGERWINDOW 1053
#define ID_EDIT_ADV_TICKMS 1054
#define ID_EDIT_ADV_MININTERVAL 1055

#define ID_STATIC_STATUS 1060
#define ID_STATIC_KEYNAME 1061

#define ID_TIMER_KEYCAPTURE 9001

static std::string ModeToStr(ClickMode m) { return m == ClickMode::Spam ? "spam" : "hold"; }
static ClickMode StrToMode(const std::string& s) { return s == "hold" ? ClickMode::Hold : ClickMode::Spam; }

static std::string SubModeToStr(HoldSubMode m) {
    switch (m) {
        case HoldSubMode::DoubleClick: return "double_click";
        case HoldSubMode::WaitForKey: return "wait_for_key";
        default: return "immediate";
    }
}
static HoldSubMode StrToSubMode(const std::string& s) {
    if (s == "double_click") return HoldSubMode::DoubleClick;
    if (s == "wait_for_key") return HoldSubMode::WaitForKey;
    return HoldSubMode::Immediate;
}

static std::string GetExeDir() {
    char buf[MAX_PATH];
    DWORD len = GetModuleFileNameA(nullptr, buf, MAX_PATH);
    if (len == 0) return ".";
    std::string full(buf, len);
    size_t pos = full.find_last_of("\\/");
    if (pos == std::string::npos) return ".";
    return full.substr(0, pos);
}

static bool EnsureDirExists(const std::string& path) {
    DWORD attr = GetFileAttributesA(path.c_str());
    if (attr != INVALID_FILE_ATTRIBUTES && (attr & FILE_ATTRIBUTE_DIRECTORY)) return true;
    return CreateDirectoryA(path.c_str(), nullptr) != 0 || GetLastError() == ERROR_ALREADY_EXISTS;
}

static void InitDataPaths() {
    g_app.exeDir = GetExeDir();
    g_app.dataDir = g_app.exeDir + "\\data";
    EnsureDirExists(g_app.dataDir);
}

static std::string SanitizeFileName(const std::string& name) {
    std::string r;
    for (char c : name) {
        if (isalnum((unsigned char)c) || c == '_' || c == '-' || c == ' ') r += c;
    }
    if (r.empty()) r = "profile";
    return r;
}

static std::string ProfileFilePath(const std::string& profileName) {
    return g_app.dataDir + "\\" + SanitizeFileName(profileName) + ".json";
}

static std::string MetaFilePath() {
    return g_app.dataDir + "\\_meta.json";
}

static bool WriteTextFile(const std::string& path, const std::string& content) {
    std::ofstream f(path, std::ios::binary | std::ios::trunc);
    if (!f.is_open()) return false;
    f << content;
    f.close();
    return true;
}

static bool ReadTextFile(const std::string& path, std::string& outContent) {
    std::ifstream f(path, std::ios::binary);
    if (!f.is_open()) return false;
    std::ostringstream ss;
    ss << f.rdbuf();
    outContent = ss.str();
    return true;
}

static std::string SettingsToJson(const ClickerSettings& s) {
    MiniJsonWriter w;
    w.Begin();
    w.KV("profileName", s.profileName);
    w.KV("mode", ModeToStr(s.mode));
    w.KV("spamAutoClickCPS", s.spamAutoClickCPS);
    w.KV("spamTriggerCPS", s.spamTriggerCPS);
    w.KV("spamDelayMs", s.spamDelayMs);
    w.KV("holdAutoClickCPS", s.holdAutoClickCPS);
    w.KV("holdDelayMs", s.holdDelayMs);
    w.KV("holdSubMode", SubModeToStr(s.holdSubMode));
    w.KV("doubleClickIntervalMs", s.doubleClickIntervalMs);
    w.KV("waitForKeyVK", (int)s.waitForKeyVK);
    w.KV("stopCheckCount", s.stopCheckCount);
    w.KV("stopCheckWindowMs", s.stopCheckWindowMs);
    w.KV("triggerSampleWindowMs", s.triggerSampleWindowMs);
    w.KV("clickerThreadTickMs", s.clickerThreadTickMs);
    w.KV("minClickIntervalMs", s.minClickIntervalMs);
    w.End();
    return w.Str();
}

static ClickerSettings JsonToSettings(const std::string& jsonText, const std::string& fallbackName) {
    ClickerSettings s;
    s.profileName = fallbackName;
    MiniJsonReader r;
    if (!r.Parse(jsonText)) return s;
    s.profileName = r.GetStr("profileName", fallbackName);
    s.mode = StrToMode(r.GetStr("mode", "spam"));
    s.spamAutoClickCPS = r.GetDouble("spamAutoClickCPS", 10.0);
    s.spamTriggerCPS = r.GetDouble("spamTriggerCPS", 5.0);
    s.spamDelayMs = r.GetInt("spamDelayMs", 100);
    s.holdAutoClickCPS = r.GetDouble("holdAutoClickCPS", 10.0);
    s.holdDelayMs = r.GetInt("holdDelayMs", 100);
    s.holdSubMode = StrToSubMode(r.GetStr("holdSubMode", "immediate"));
    s.doubleClickIntervalMs = r.GetInt("doubleClickIntervalMs", 300);
    s.waitForKeyVK = (UINT)r.GetInt("waitForKeyVK", VK_SHIFT);
    s.stopCheckCount = r.GetInt("stopCheckCount", 3);
    s.stopCheckWindowMs = r.GetInt("stopCheckWindowMs", 150);
    s.triggerSampleWindowMs = r.GetInt("triggerSampleWindowMs", 400);
    s.clickerThreadTickMs = r.GetInt("clickerThreadTickMs", 2);
    s.minClickIntervalMs = r.GetInt("minClickIntervalMs", 1);
    return s;
}

static bool SaveProfile(const ClickerSettings& s) {
    EnsureDirExists(g_app.dataDir);
    return WriteTextFile(ProfileFilePath(s.profileName), SettingsToJson(s));
}

static bool LoadProfile(const std::string& profileName, ClickerSettings& outSettings) {
    std::string content;
    if (!ReadTextFile(ProfileFilePath(profileName), content)) return false;
    outSettings = JsonToSettings(content, profileName);
    return true;
}

static bool DeleteProfileFile(const std::string& profileName) {
    return DeleteFileA(ProfileFilePath(profileName).c_str()) != 0;
}

static bool ProfileExists(const std::string& profileName) {
    DWORD attr = GetFileAttributesA(ProfileFilePath(profileName).c_str());
    return attr != INVALID_FILE_ATTRIBUTES;
}

static std::vector<std::string> ListProfiles() {
    std::vector<std::string> result;
    std::string searchPath = g_app.dataDir + "\\*.json";
    WIN32_FIND_DATAA fd;
    HANDLE hFind = FindFirstFileA(searchPath.c_str(), &fd);
    if (hFind == INVALID_HANDLE_VALUE) return result;
    do {
        std::string fname(fd.cFileName);
        if (fname == "_meta.json") continue;
        size_t dot = fname.find_last_of('.');
        if (dot != std::string::npos) fname = fname.substr(0, dot);
        result.push_back(fname);
    } while (FindNextFileA(hFind, &fd));
    FindClose(hFind);
    std::sort(result.begin(), result.end());
    return result;
}

static void SaveLastProfileMeta(const std::string& profileName) {
    MiniJsonWriter w;
    w.Begin();
    w.KV("lastProfile", profileName);
    w.End();
    WriteTextFile(MetaFilePath(), w.Str());
}

static std::string LoadLastProfileMeta() {
    std::string content;
    if (!ReadTextFile(MetaFilePath(), content)) return "";
    MiniJsonReader r;
    if (!r.Parse(content)) return "";
    return r.GetStr("lastProfile", "");
}

static void EnsureDefaultProfileExists() {
    if (!ProfileExists("Default")) {
        ClickerSettings def;
        def.profileName = "Default";
        SaveProfile(def);
    }
}

static std::string VkToDisplayName(UINT vk) {
    if (vk == 0) return "(none)";
    char name[128] = {0};
    UINT scanCode = MapVirtualKeyA(vk, MAPVK_VK_TO_VSC);
    LONG lparam = (LONG)(scanCode << 16);
    switch (vk) {
        case VK_LEFT: case VK_UP: case VK_RIGHT: case VK_DOWN:
        case VK_PRIOR: case VK_NEXT: case VK_END: case VK_HOME:
        case VK_INSERT: case VK_DELETE: case VK_DIVIDE: case VK_NUMLOCK:
            lparam |= (1 << 24);
            break;
    }
    if (GetKeyNameTextA(lparam, name, sizeof(name)) > 0) {
        return std::string(name);
    }
    return "VK_" + std::to_string(vk);
}

static HWND MakeStatic(HWND parent, const char* text, int x, int y, int w, int h) {
    return CreateWindowExA(0, "STATIC", text, WS_CHILD | WS_VISIBLE,
        x, y, w, h, parent, nullptr, GetModuleHandle(nullptr), nullptr);
}

static HWND MakeEdit(HWND parent, int id, int x, int y, int w, int h) {
    return CreateWindowExA(WS_EX_CLIENTEDGE, "EDIT", "", WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL,
        x, y, w, h, parent, (HMENU)(INT_PTR)id, GetModuleHandle(nullptr), nullptr);
}

static HWND MakeButton(HWND parent, int id, const char* text, int x, int y, int w, int h, DWORD extraStyle = 0) {
    return CreateWindowExA(0, "BUTTON", text, WS_CHILD | WS_VISIBLE | WS_TABSTOP | extraStyle,
        x, y, w, h, parent, (HMENU)(INT_PTR)id, GetModuleHandle(nullptr), nullptr);
}

static HWND MakeGroupBox(HWND parent, const char* text, int x, int y, int w, int h) {
    return CreateWindowExA(0, "BUTTON", text, WS_CHILD | WS_VISIBLE | BS_GROUPBOX,
        x, y, w, h, parent, nullptr, GetModuleHandle(nullptr), nullptr);
}

static HWND MakeCombo(HWND parent, int id, int x, int y, int w, int h) {
    return CreateWindowExA(WS_EX_CLIENTEDGE, "COMBOBOX", "",
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | CBS_DROPDOWNLIST | WS_VSCROLL,
        x, y, w, h, parent, (HMENU)(INT_PTR)id, GetModuleHandle(nullptr), nullptr);
}

static void SetEditDouble(HWND h, double v) {
    char buf[64];
    snprintf(buf, sizeof(buf), "%.3g", v);
    SetWindowTextA(h, buf);
}
static void SetEditInt(HWND h, int v) {
    char buf[32];
    snprintf(buf, sizeof(buf), "%d", v);
    SetWindowTextA(h, buf);
}
static double GetEditDouble(HWND h, double def) {
    char buf[64];
    GetWindowTextA(h, buf, sizeof(buf));
    try {
        std::string s(buf);
        if (s.empty()) return def;
        return std::stod(s);
    } catch (...) { return def; }
}
static int GetEditInt(HWND h, int def) {
    char buf[64];
    GetWindowTextA(h, buf, sizeof(buf));
    try {
        std::string s(buf);
        if (s.empty()) return def;
        return std::stoi(s);
    } catch (...) { return def; }
}

struct UiHandles {
    HWND radioSpam, radioHold;

    HWND grpSpam;
    HWND lblSpamAutoCps, editSpamAutoCps;
    HWND lblSpamTriggerCps, editSpamTriggerCps;
    HWND lblSpamDelay, editSpamDelay;

    HWND grpHold;
    HWND lblHoldAutoCps, editHoldAutoCps;
    HWND lblHoldDelay, editHoldDelay;
    HWND radioHoldImmediate, radioHoldDoubleClick, radioHoldWaitKey;
    HWND lblDoubleClickInterval, editDoubleClickInterval;
    HWND lblKeySelect, btnKeySelect;

    HWND grpProfile;
    HWND comboProfile;
    HWND btnNewProfile, btnDeleteProfile, btnSaveProfile;

    HWND btnAdvancedToggle;
    HWND grpAdvanced;
    HWND lblStopCheckCount, editStopCheckCount;
    HWND lblStopCheckWindow, editStopCheckWindow;
    HWND lblTriggerWindow, editTriggerWindow;
    HWND lblTickMs, editTickMs;
    HWND lblMinInterval, editMinInterval;

    HWND btnEnable;
    HWND lblStatus;

    HFONT font;
};

static UiHandles g_ui;
static bool g_advancedExpanded = false;
static bool g_suppressChangeEvents = false;

static const int WIN_WIDTH = 480;
static const int WIN_HEIGHT_COLLAPSED = 560;
static const int WIN_HEIGHT_EXPANDED = 720;

static void CreateAllControls(HWND hwnd) {
    HINSTANCE hInst = GetModuleHandle(nullptr);
    g_ui.font = CreateFontA(-13, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
        DEFAULT_QUALITY, DEFAULT_PITCH | FF_SWISS, "MS Shell Dlg");

    int y = 10;
    int leftMargin = 12;
    int fullWidth = WIN_WIDTH - 2 * leftMargin - 6;

    g_ui.grpProfile = MakeGroupBox(hwnd, "Profile", leftMargin, y, fullWidth, 60);
    g_ui.comboProfile = MakeCombo(hwnd, ID_COMBO_PROFILE, leftMargin + 12, y + 22, 180, 200);
    g_ui.btnNewProfile = MakeButton(hwnd, ID_BUTTON_NEWPROFILE, "New", leftMargin + 200, y + 21, 60, 24);
    g_ui.btnDeleteProfile = MakeButton(hwnd, ID_BUTTON_DELETEPROFILE, "Delete", leftMargin + 265, y + 21, 60, 24);
    g_ui.btnSaveProfile = MakeButton(hwnd, ID_BUTTON_SAVEPROFILE, "Save", leftMargin + 330, y + 21, 60, 24);
    y += 70;

    g_ui.radioSpam = CreateWindowExA(0, "BUTTON", "Spam Mode", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_AUTORADIOBUTTON | WS_GROUP,
        leftMargin, y, 120, 20, hwnd, (HMENU)ID_RADIO_SPAM, hInst, nullptr);
    g_ui.radioHold = CreateWindowExA(0, "BUTTON", "Hold Mode", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_AUTORADIOBUTTON,
        leftMargin + 130, y, 120, 20, hwnd, (HMENU)ID_RADIO_HOLD, hInst, nullptr);
    y += 28;

    int grpY = y;
    g_ui.grpSpam = MakeGroupBox(hwnd, "Spam Mode Settings", leftMargin, grpY, fullWidth, 130);
    int sy = grpY + 24;
    g_ui.lblSpamAutoCps = MakeStatic(hwnd, "Autoclicker CPS:", leftMargin + 14, sy, 150, 18);
    g_ui.editSpamAutoCps = MakeEdit(hwnd, ID_EDIT_SPAM_AUTOCPS, leftMargin + 170, sy - 2, 100, 22);
    sy += 30;
    g_ui.lblSpamTriggerCps = MakeStatic(hwnd, "Trigger CPS:", leftMargin + 14, sy, 150, 18);
    g_ui.editSpamTriggerCps = MakeEdit(hwnd, ID_EDIT_SPAM_TRIGGERCPS, leftMargin + 170, sy - 2, 100, 22);
    sy += 30;
    g_ui.lblSpamDelay = MakeStatic(hwnd, "Delay (ms):", leftMargin + 14, sy, 150, 18);
    g_ui.editSpamDelay = MakeEdit(hwnd, ID_EDIT_SPAM_DELAY, leftMargin + 170, sy - 2, 100, 22);

    g_ui.grpHold = MakeGroupBox(hwnd, "Hold Mode Settings", leftMargin, grpY, fullWidth, 230);
    int hy = grpY + 24;
    g_ui.lblHoldAutoCps = MakeStatic(hwnd, "Autoclicker CPS:", leftMargin + 14, hy, 150, 18);
    g_ui.editHoldAutoCps = MakeEdit(hwnd, ID_EDIT_HOLD_AUTOCPS, leftMargin + 170, hy - 2, 100, 22);
    hy += 30;
    g_ui.lblHoldDelay = MakeStatic(hwnd, "Delay (ms):", leftMargin + 14, hy, 150, 18);
    g_ui.editHoldDelay = MakeEdit(hwnd, ID_EDIT_HOLD_DELAY, leftMargin + 170, hy - 2, 100, 22);
    hy += 34;

    g_ui.radioHoldImmediate = CreateWindowExA(0, "BUTTON", "Immediate", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_AUTORADIOBUTTON | WS_GROUP,
        leftMargin + 14, hy, 100, 20, hwnd, (HMENU)ID_RADIO_HOLD_IMMEDIATE, hInst, nullptr);
    g_ui.radioHoldDoubleClick = CreateWindowExA(0, "BUTTON", "Double Click", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_AUTORADIOBUTTON,
        leftMargin + 120, hy, 110, 20, hwnd, (HMENU)ID_RADIO_HOLD_DOUBLECLICK, hInst, nullptr);
    g_ui.radioHoldWaitKey = CreateWindowExA(0, "BUTTON", "Wait For Button", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_AUTORADIOBUTTON,
        leftMargin + 240, hy, 130, 20, hwnd, (HMENU)ID_RADIO_HOLD_WAITKEY, hInst, nullptr);
    hy += 30;

    g_ui.lblDoubleClickInterval = MakeStatic(hwnd, "Double Click Interval (ms):", leftMargin + 14, hy, 180, 18);
    g_ui.editDoubleClickInterval = MakeEdit(hwnd, ID_EDIT_DOUBLECLICK_INTERVAL, leftMargin + 200, hy - 2, 100, 22);
    hy += 30;

    g_ui.lblKeySelect = MakeStatic(hwnd, "Trigger Key:", leftMargin + 14, hy, 100, 18);
    g_ui.btnKeySelect = MakeButton(hwnd, ID_BUTTON_KEYSELECT, "Shift", leftMargin + 120, hy - 3, 140, 24);

    y = grpY + 240;

    g_ui.btnAdvancedToggle = MakeButton(hwnd, ID_BUTTON_ADVANCED_TOGGLE, "Show Advanced Settings >>", leftMargin, y, fullWidth, 24);
    y += 32;

    g_ui.grpAdvanced = MakeGroupBox(hwnd, "Advanced Settings", leftMargin, y, fullWidth, 160);
    int ay = y + 24;
    g_ui.lblStopCheckCount = MakeStatic(hwnd, "Stop-check count:", leftMargin + 14, ay, 170, 18);
    g_ui.editStopCheckCount = MakeEdit(hwnd, ID_EDIT_ADV_STOPCHECKCOUNT, leftMargin + 190, ay - 2, 80, 22);
    ay += 28;
    g_ui.lblStopCheckWindow = MakeStatic(hwnd, "Stop-check window (ms):", leftMargin + 14, ay, 170, 18);
    g_ui.editStopCheckWindow = MakeEdit(hwnd, ID_EDIT_ADV_STOPCHECKWINDOW, leftMargin + 190, ay - 2, 80, 22);
    ay += 28;
    g_ui.lblTriggerWindow = MakeStatic(hwnd, "Trigger sample window (ms):", leftMargin + 14, ay, 170, 18);
    g_ui.editTriggerWindow = MakeEdit(hwnd, ID_EDIT_ADV_TRIGGERWINDOW, leftMargin + 190, ay - 2, 80, 22);
    ay += 28;
    g_ui.lblTickMs = MakeStatic(hwnd, "Internal tick (ms):", leftMargin + 14, ay, 170, 18);
    g_ui.editTickMs = MakeEdit(hwnd, ID_EDIT_ADV_TICKMS, leftMargin + 190, ay - 2, 80, 22);
    ay += 28;
    g_ui.lblMinInterval = MakeStatic(hwnd, "Min click interval (ms):", leftMargin + 14, ay, 170, 18);
    g_ui.editMinInterval = MakeEdit(hwnd, ID_EDIT_ADV_MININTERVAL, leftMargin + 190, ay - 2, 80, 22);

    int bottomY = WIN_HEIGHT_COLLAPSED - 90;
    g_ui.lblStatus = CreateWindowExA(0, "STATIC", "Status: Disabled", WS_CHILD | WS_VISIBLE | SS_CENTER,
        leftMargin, bottomY, fullWidth, 22, hwnd, (HMENU)ID_STATIC_STATUS, hInst, nullptr);
    g_ui.btnEnable = MakeButton(hwnd, ID_BUTTON_ENABLE, "Enable", leftMargin, bottomY + 28, fullWidth, 36, BS_DEFPUSHBUTTON);

    HWND controls[] = {
        g_ui.radioSpam, g_ui.radioHold, g_ui.grpSpam, g_ui.lblSpamAutoCps, g_ui.editSpamAutoCps,
        g_ui.lblSpamTriggerCps, g_ui.editSpamTriggerCps, g_ui.lblSpamDelay, g_ui.editSpamDelay,
        g_ui.grpHold, g_ui.lblHoldAutoCps, g_ui.editHoldAutoCps, g_ui.lblHoldDelay, g_ui.editHoldDelay,
        g_ui.radioHoldImmediate, g_ui.radioHoldDoubleClick, g_ui.radioHoldWaitKey,
        g_ui.lblDoubleClickInterval, g_ui.editDoubleClickInterval, g_ui.lblKeySelect, g_ui.btnKeySelect,
        g_ui.grpProfile, g_ui.comboProfile, g_ui.btnNewProfile, g_ui.btnDeleteProfile, g_ui.btnSaveProfile,
        g_ui.btnAdvancedToggle, g_ui.grpAdvanced,
        g_ui.lblStopCheckCount, g_ui.editStopCheckCount, g_ui.lblStopCheckWindow, g_ui.editStopCheckWindow,
        g_ui.lblTriggerWindow, g_ui.editTriggerWindow, g_ui.lblTickMs, g_ui.editTickMs,
        g_ui.lblMinInterval, g_ui.editMinInterval, g_ui.btnEnable, g_ui.lblStatus
    };
    for (HWND c : controls) {
        if (c) SendMessage(c, WM_SETFONT, (WPARAM)g_ui.font, TRUE);
    }
}

static void ShowWnd(HWND h, bool show) {
    if (h) ShowWindow(h, show ? SW_SHOW : SW_HIDE);
}

static void RefreshModeVisibility() {
    ClickerSettings s;
    {
        std::lock_guard<std::mutex> lock(g_app.settingsMutex);
        s = g_app.settings;
    }
    bool spam = (s.mode == ClickMode::Spam);
    ShowWnd(g_ui.grpSpam, spam);
    ShowWnd(g_ui.lblSpamAutoCps, spam);
    ShowWnd(g_ui.editSpamAutoCps, spam);
    ShowWnd(g_ui.lblSpamTriggerCps, spam);
    ShowWnd(g_ui.editSpamTriggerCps, spam);
    ShowWnd(g_ui.lblSpamDelay, spam);
    ShowWnd(g_ui.editSpamDelay, spam);

    ShowWnd(g_ui.grpHold, !spam);
    ShowWnd(g_ui.lblHoldAutoCps, !spam);
    ShowWnd(g_ui.editHoldAutoCps, !spam);
    ShowWnd(g_ui.lblHoldDelay, !spam);
    ShowWnd(g_ui.editHoldDelay, !spam);
    ShowWnd(g_ui.radioHoldImmediate, !spam);
    ShowWnd(g_ui.radioHoldDoubleClick, !spam);
    ShowWnd(g_ui.radioHoldWaitKey, !spam);

    bool showDoubleClickInterval = !spam && s.holdSubMode == HoldSubMode::DoubleClick;
    bool showKeySelect = !spam && s.holdSubMode == HoldSubMode::WaitForKey;
    ShowWnd(g_ui.lblDoubleClickInterval, showDoubleClickInterval);
    ShowWnd(g_ui.editDoubleClickInterval, showDoubleClickInterval);
    ShowWnd(g_ui.lblKeySelect, showKeySelect);
    ShowWnd(g_ui.btnKeySelect, showKeySelect);

    ShowWnd(g_ui.grpAdvanced, g_advancedExpanded);
    ShowWnd(g_ui.lblStopCheckCount, g_advancedExpanded);
    ShowWnd(g_ui.editStopCheckCount, g_advancedExpanded);
    ShowWnd(g_ui.lblStopCheckWindow, g_advancedExpanded);
    ShowWnd(g_ui.editStopCheckWindow, g_advancedExpanded);
    ShowWnd(g_ui.lblTriggerWindow, g_advancedExpanded);
    ShowWnd(g_ui.editTriggerWindow, g_advancedExpanded);
    ShowWnd(g_ui.lblTickMs, g_advancedExpanded);
    ShowWnd(g_ui.editTickMs, g_advancedExpanded);
    ShowWnd(g_ui.lblMinInterval, g_advancedExpanded);
    ShowWnd(g_ui.editMinInterval, g_advancedExpanded);

    SetWindowTextA(g_ui.btnAdvancedToggle, g_advancedExpanded ? "Hide Advanced Settings <<" : "Show Advanced Settings >>");
}

static void RelayoutWindow(HWND hwnd) {
    RECT rc;
    GetWindowRect(hwnd, &rc);
    int targetHeight = g_advancedExpanded ? WIN_HEIGHT_EXPANDED : WIN_HEIGHT_COLLAPSED;
    SetWindowPos(hwnd, nullptr, 0, 0, WIN_WIDTH, targetHeight, SWP_NOMOVE | SWP_NOZORDER);

    int leftMargin = 12;
    int fullWidth = WIN_WIDTH - 2 * leftMargin - 6;
    int bottomY = targetHeight - 90;
    SetWindowPos(g_ui.lblStatus, nullptr, leftMargin, bottomY, fullWidth, 22, SWP_NOZORDER);
    SetWindowPos(g_ui.btnEnable, nullptr, leftMargin, bottomY + 28, fullWidth, 36, SWP_NOZORDER);
}

static void PushSettingsToUi() {
    g_suppressChangeEvents = true;
    ClickerSettings s;
    {
        std::lock_guard<std::mutex> lock(g_app.settingsMutex);
        s = g_app.settings;
    }

    SendMessage(g_ui.radioSpam, BM_SETCHECK, s.mode == ClickMode::Spam ? BST_CHECKED : BST_UNCHECKED, 0);
    SendMessage(g_ui.radioHold, BM_SETCHECK, s.mode == ClickMode::Hold ? BST_CHECKED : BST_UNCHECKED, 0);

    SetEditDouble(g_ui.editSpamAutoCps, s.spamAutoClickCPS);
    SetEditDouble(g_ui.editSpamTriggerCps, s.spamTriggerCPS);
    SetEditInt(g_ui.editSpamDelay, s.spamDelayMs);

    SetEditDouble(g_ui.editHoldAutoCps, s.holdAutoClickCPS);
    SetEditInt(g_ui.editHoldDelay, s.holdDelayMs);

    SendMessage(g_ui.radioHoldImmediate, BM_SETCHECK, s.holdSubMode == HoldSubMode::Immediate ? BST_CHECKED : BST_UNCHECKED, 0);
    SendMessage(g_ui.radioHoldDoubleClick, BM_SETCHECK, s.holdSubMode == HoldSubMode::DoubleClick ? BST_CHECKED : BST_UNCHECKED, 0);
    SendMessage(g_ui.radioHoldWaitKey, BM_SETCHECK, s.holdSubMode == HoldSubMode::WaitForKey ? BST_CHECKED : BST_UNCHECKED, 0);

    SetEditInt(g_ui.editDoubleClickInterval, s.doubleClickIntervalMs);
    SetWindowTextA(g_ui.btnKeySelect, VkToDisplayName(s.waitForKeyVK).c_str());

    SetEditInt(g_ui.editStopCheckCount, s.stopCheckCount);
    SetEditInt(g_ui.editStopCheckWindow, s.stopCheckWindowMs);
    SetEditInt(g_ui.editTriggerWindow, s.triggerSampleWindowMs);
    SetEditInt(g_ui.editTickMs, s.clickerThreadTickMs);
    SetEditInt(g_ui.editMinInterval, s.minClickIntervalMs);

    RefreshModeVisibility();
    g_suppressChangeEvents = false;
}

static void PullUiToSettings() {
    std::lock_guard<std::mutex> lock(g_app.settingsMutex);
    ClickerSettings& s = g_app.settings;

    s.mode = (SendMessage(g_ui.radioHold, BM_GETCHECK, 0, 0) == BST_CHECKED) ? ClickMode::Hold : ClickMode::Spam;

    s.spamAutoClickCPS = GetEditDouble(g_ui.editSpamAutoCps, s.spamAutoClickCPS);
    s.spamTriggerCPS = GetEditDouble(g_ui.editSpamTriggerCps, s.spamTriggerCPS);
    s.spamDelayMs = GetEditInt(g_ui.editSpamDelay, s.spamDelayMs);

    s.holdAutoClickCPS = GetEditDouble(g_ui.editHoldAutoCps, s.holdAutoClickCPS);
    s.holdDelayMs = GetEditInt(g_ui.editHoldDelay, s.holdDelayMs);

    if (SendMessage(g_ui.radioHoldDoubleClick, BM_GETCHECK, 0, 0) == BST_CHECKED) s.holdSubMode = HoldSubMode::DoubleClick;
    else if (SendMessage(g_ui.radioHoldWaitKey, BM_GETCHECK, 0, 0) == BST_CHECKED) s.holdSubMode = HoldSubMode::WaitForKey;
    else s.holdSubMode = HoldSubMode::Immediate;

    s.doubleClickIntervalMs = GetEditInt(g_ui.editDoubleClickInterval, s.doubleClickIntervalMs);

    s.stopCheckCount = GetEditInt(g_ui.editStopCheckCount, s.stopCheckCount);
    s.stopCheckWindowMs = GetEditInt(g_ui.editStopCheckWindow, s.stopCheckWindowMs);
    s.triggerSampleWindowMs = GetEditInt(g_ui.editTriggerWindow, s.triggerSampleWindowMs);
    s.clickerThreadTickMs = GetEditInt(g_ui.editTickMs, s.clickerThreadTickMs);
    s.minClickIntervalMs = GetEditInt(g_ui.editMinInterval, s.minClickIntervalMs);

    if (s.spamAutoClickCPS < 0.1) s.spamAutoClickCPS = 0.1;
    if (s.holdAutoClickCPS < 0.1) s.holdAutoClickCPS = 0.1;
    if (s.spamDelayMs < 0) s.spamDelayMs = 0;
    if (s.holdDelayMs < 0) s.holdDelayMs = 0;
    if (s.doubleClickIntervalMs < 1) s.doubleClickIntervalMs = 1;
    if (s.stopCheckCount < 1) s.stopCheckCount = 1;
    if (s.stopCheckWindowMs < 1) s.stopCheckWindowMs = 1;
    if (s.triggerSampleWindowMs < 1) s.triggerSampleWindowMs = 1;
    if (s.clickerThreadTickMs < 1) s.clickerThreadTickMs = 1;
    if (s.minClickIntervalMs < 1) s.minClickIntervalMs = 1;
}

static void RefreshProfileCombo(const std::string& selectName) {
    SendMessage(g_ui.comboProfile, CB_RESETCONTENT, 0, 0);
    std::vector<std::string> profiles = ListProfiles();
    int selectIndex = -1;
    for (size_t i = 0; i < profiles.size(); i++) {
        SendMessageA(g_ui.comboProfile, CB_ADDSTRING, 0, (LPARAM)profiles[i].c_str());
        if (profiles[i] == selectName) selectIndex = (int)i;
    }
    if (selectIndex >= 0) {
        SendMessage(g_ui.comboProfile, CB_SETCURSEL, selectIndex, 0);
    } else if (!profiles.empty()) {
        SendMessage(g_ui.comboProfile, CB_SETCURSEL, 0, 0);
    }
}

static void SetStatusText(const std::string& text) {
    SetWindowTextA(g_ui.lblStatus, text.c_str());
}

static void UpdateEnableButtonText() {
    bool en = g_app.AC_IsEnabled ? g_app.AC_IsEnabled() != FALSE : false;
    SetWindowTextA(g_ui.btnEnable, en ? "Disable" : "Enable");
}

static void MarkUnsavedAndDisable(HWND hwnd) {
    (void)hwnd;
    if (g_suppressChangeEvents) return;
    g_app.unsavedChanges.store(true);
    bool en = g_app.AC_IsEnabled ? g_app.AC_IsEnabled() != FALSE : false;
    if (en) {
        if (g_app.AC_Disable) g_app.AC_Disable();
        UpdateEnableButtonText();
        SetStatusText("Status: Disabled (settings changed)");
    }
}

enum class ConfirmResult { SaveAndProceed, DiscardAndProceed, Cancel };

static ConfirmResult AskSaveUnsavedChanges(HWND hwnd, const std::string& profileName) {
    std::string msg = "You have unsaved changes to profile \"" + profileName +
        "\".\n\nYes = Save changes and proceed\nNo = Discard changes and proceed\nCancel = Stay here";
    int result = MessageBoxA(hwnd, msg.c_str(), "Unsaved Changes", MB_YESNOCANCEL | MB_ICONWARNING);
    if (result == IDYES) return ConfirmResult::SaveAndProceed;
    if (result == IDNO) return ConfirmResult::DiscardAndProceed;
    return ConfirmResult::Cancel;
}

static bool DoSaveCurrentProfile() {
    ClickerSettings s;
    {
        std::lock_guard<std::mutex> lock(g_app.settingsMutex);
        s = g_app.settings;
    }
    bool ok = SaveProfile(s);
    if (ok) g_app.unsavedChanges.store(false);
    return ok;
}

static bool PromptForProfileName(HWND parent, std::string& outName) {
    HINSTANCE hInst = GetModuleHandle(nullptr);

    char nameBuf[256] = "";
    bool accepted = false;

    HWND hPrompt = CreateWindowExA(WS_EX_DLGMODALFRAME | WS_EX_TOPMOST, "#32770", "New Profile",
        WS_POPUP | WS_CAPTION | WS_SYSMENU | DS_MODALFRAME,
        200, 200, 320, 140, parent, nullptr, hInst, nullptr);

    if (!hPrompt) return false;

    HWND lbl = CreateWindowExA(0, "STATIC", "Enter a name for the new profile:",
        WS_CHILD | WS_VISIBLE, 12, 12, 280, 20, hPrompt, nullptr, hInst, nullptr);
    HWND edit = CreateWindowExA(WS_EX_CLIENTEDGE, "EDIT", "",
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL, 12, 36, 280, 24, hPrompt, (HMENU)201, hInst, nullptr);
    HWND okBtn = CreateWindowExA(0, "BUTTON", "OK", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_DEFPUSHBUTTON,
        70, 74, 80, 26, hPrompt, (HMENU)(INT_PTR)IDOK, hInst, nullptr);
    HWND cancelBtn = CreateWindowExA(0, "BUTTON", "Cancel", WS_CHILD | WS_VISIBLE | WS_TABSTOP,
        160, 74, 80, 26, hPrompt, (HMENU)(INT_PTR)IDCANCEL, hInst, nullptr);

    HFONT font = CreateFontA(-13, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, DEFAULT_QUALITY, DEFAULT_PITCH | FF_SWISS, "MS Shell Dlg");
    SendMessage(lbl, WM_SETFONT, (WPARAM)font, TRUE);
    SendMessage(edit, WM_SETFONT, (WPARAM)font, TRUE);
    SendMessage(okBtn, WM_SETFONT, (WPARAM)font, TRUE);
    SendMessage(cancelBtn, WM_SETFONT, (WPARAM)font, TRUE);

    RECT parentRc, dlgRc;
    GetWindowRect(parent, &parentRc);
    GetWindowRect(hPrompt, &dlgRc);
    int dw = dlgRc.right - dlgRc.left;
    int dh = dlgRc.bottom - dlgRc.top;
    int px = parentRc.left + ((parentRc.right - parentRc.left) - dw) / 2;
    int py = parentRc.top + ((parentRc.bottom - parentRc.top) - dh) / 2;
    SetWindowPos(hPrompt, nullptr, px, py, 0, 0, SWP_NOSIZE | SWP_NOZORDER);

    ShowWindow(hPrompt, SW_SHOW);
    SetFocus(edit);
    EnableWindow(parent, FALSE);

    bool done = false;
    MSG msg;
    while (!done && GetMessage(&msg, nullptr, 0, 0)) {
        if (msg.hwnd == hPrompt || IsChild(hPrompt, msg.hwnd)) {
            if (msg.message == WM_COMMAND) {
                int id = LOWORD(msg.wParam);
                if (id == IDOK) {
                    GetWindowTextA(edit, nameBuf, sizeof(nameBuf));
                    accepted = true;
                    done = true;
                } else if (id == IDCANCEL) {
                    accepted = false;
                    done = true;
                }
            }
            if (msg.message == WM_KEYDOWN && msg.wParam == VK_RETURN) {
                GetWindowTextA(edit, nameBuf, sizeof(nameBuf));
                accepted = true;
                done = true;
            }
            if (msg.message == WM_KEYDOWN && msg.wParam == VK_ESCAPE) {
                accepted = false;
                done = true;
            }
            if (msg.message == WM_CLOSE) {
                accepted = false;
                done = true;
            }
            if (!IsDialogMessage(hPrompt, &msg)) {
                TranslateMessage(&msg);
                DispatchMessage(&msg);
            }
        } else {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
        }
    }

    EnableWindow(parent, TRUE);
    SetForegroundWindow(parent);
    DestroyWindow(hPrompt);
    DeleteObject(font);

    outName = TrimCopy(std::string(nameBuf));
    return accepted && !outName.empty();
}

static bool g_waitingForKeyCapture = false;
static std::string g_keySelectOldText;

static void BeginKeyCapture(HWND hwnd) {
    char buf[128];
    GetWindowTextA(g_ui.btnKeySelect, buf, sizeof(buf));
    g_keySelectOldText = buf;
    g_waitingForKeyCapture = true;
    SetWindowTextA(g_ui.btnKeySelect, "Press any key...");
    EnableWindow(g_ui.btnEnable, FALSE);
    EnableWindow(g_ui.comboProfile, FALSE);
    SetTimer(hwnd, ID_TIMER_KEYCAPTURE, 15000, nullptr);
    SetFocus(hwnd);
}

static void EndKeyCapture(HWND hwnd, bool applied, UINT capturedVk) {
    g_waitingForKeyCapture = false;
    KillTimer(hwnd, ID_TIMER_KEYCAPTURE);
    EnableWindow(g_ui.btnEnable, TRUE);
    EnableWindow(g_ui.comboProfile, TRUE);
    if (applied) {
        {
            std::lock_guard<std::mutex> lock(g_app.settingsMutex);
            g_app.settings.waitForKeyVK = capturedVk;
        }
        SetWindowTextA(g_ui.btnKeySelect, VkToDisplayName(capturedVk).c_str());
        MarkUnsavedAndDisable(hwnd);
    } else {
        SetWindowTextA(g_ui.btnKeySelect, g_keySelectOldText.c_str());
    }
}

static std::string GetSelectedProfileName() {
    int idx = (int)SendMessage(g_ui.comboProfile, CB_GETCURSEL, 0, 0);
    if (idx == CB_ERR) return "";
    char buf[256];
    SendMessageA(g_ui.comboProfile, CB_GETLBTEXT, idx, (LPARAM)buf);
    return std::string(buf);
}

static void LoadProfileIntoAppAndUi(const std::string& profileName) {
    ClickerSettings loaded;
    if (LoadProfile(profileName, loaded)) {
        std::lock_guard<std::mutex> lock(g_app.settingsMutex);
        g_app.settings = loaded;
    }
    g_app.unsavedChanges.store(false);
    PushSettingsToUi();
    SaveLastProfileMeta(profileName);
    if (g_app.AC_SetSettings) {
        std::lock_guard<std::mutex> lock(g_app.settingsMutex);
        g_app.AC_SetSettings(&g_app.settings);
    }
}

static bool TryDoProfileSwitch(HWND hwnd, const std::string& newProfileName) {
    if (g_app.unsavedChanges.load()) {
        std::string currentProfile;
        {
            std::lock_guard<std::mutex> lock(g_app.settingsMutex);
            currentProfile = g_app.settings.profileName;
        }
        ConfirmResult r = AskSaveUnsavedChanges(hwnd, currentProfile);
        if (r == ConfirmResult::Cancel) return false;
        if (r == ConfirmResult::SaveAndProceed) {
            if (!DoSaveCurrentProfile()) {
                MessageBoxA(hwnd, "Failed to save the current profile.", "Error", MB_ICONERROR);
                return false;
            }
        }
    }
    LoadProfileIntoAppAndUi(newProfileName);
    return true;
}

static void UpdateStatusForRunState(RunState rs, bool enabled) {
    if (!enabled) {
        SetStatusText("Status: Disabled");
        return;
    }
    switch (rs) {
        case RunState::Idle: SetStatusText("Status: Enabled - Idle"); break;
        case RunState::ArmedWaitingDelay: SetStatusText("Status: Enabled - Waiting delay..."); break;
        case RunState::WaitingSecondClick: SetStatusText("Status: Enabled - Waiting for 2nd click..."); break;
        case RunState::Active: SetStatusText("Status: Enabled - CLICKING"); break;
    }
}

static void TryEnableProgram(HWND hwnd) {
    if (g_app.unsavedChanges.load()) {
        std::string currentProfile;
        {
            std::lock_guard<std::mutex> lock(g_app.settingsMutex);
            currentProfile = g_app.settings.profileName;
        }
        ConfirmResult r = AskSaveUnsavedChanges(hwnd, currentProfile);
        if (r == ConfirmResult::Cancel) return;
        if (r == ConfirmResult::SaveAndProceed) {
            if (!DoSaveCurrentProfile()) {
                MessageBoxA(hwnd, "Failed to save the current profile.", "Error", MB_ICONERROR);
                return;
            }
        }
    }
    PullUiToSettings();
    {
        std::lock_guard<std::mutex> lock(g_app.settingsMutex);
        if (g_app.AC_SetSettings) g_app.AC_SetSettings(&g_app.settings);
    }
    if (g_app.AC_Enable) g_app.AC_Enable();
    UpdateEnableButtonText();
    UpdateStatusForRunState(RunState::Idle, true);
}

static void DisableProgram() {
    if (g_app.AC_Disable) g_app.AC_Disable();
    UpdateEnableButtonText();
    UpdateStatusForRunState(RunState::Idle, false);
}

static FARPROC GetProcAddressFlex(HMODULE h, const char* name) {
    FARPROC p = GetProcAddress(h, name);
    if (!p) {
        std::string alt = "_";
        alt += name;
        p = GetProcAddress(h, alt.c_str());
    }
    return p;
}

static void UnloadEngineDll();

static bool LoadEngineDll() {
    std::string dllPath = g_app.exeDir + "\\ac_engine.dll";
    g_app.hEngine = LoadLibraryA(dllPath.c_str());
    if (!g_app.hEngine) {
        g_app.hEngine = LoadLibraryA("ac_engine.dll");
    }
    if (!g_app.hEngine) return false;

    g_app.AC_Init = (AC_InitFn)GetProcAddressFlex(g_app.hEngine, "AC_Init");
    g_app.AC_Shutdown = (AC_ShutdownFn)GetProcAddressFlex(g_app.hEngine, "AC_Shutdown");
    g_app.AC_SetSettings = (AC_SetSettingsFn)GetProcAddressFlex(g_app.hEngine, "AC_SetSettings");
    g_app.AC_Enable = (AC_EnableFn)GetProcAddressFlex(g_app.hEngine, "AC_Enable");
    g_app.AC_Disable = (AC_DisableFn)GetProcAddressFlex(g_app.hEngine, "AC_Disable");
    g_app.AC_IsEnabled = (AC_IsEnabledFn)GetProcAddressFlex(g_app.hEngine, "AC_IsEnabled");
    g_app.AC_GetRunState = (AC_GetRunStateFn)GetProcAddressFlex(g_app.hEngine, "AC_GetRunState");

    if (!g_app.AC_Init || !g_app.AC_Shutdown || !g_app.AC_SetSettings ||
        !g_app.AC_Enable || !g_app.AC_Disable || !g_app.AC_IsEnabled ||
        !g_app.AC_GetRunState) {
        UnloadEngineDll();
        return false;
    }
    return true;
}

static void UnloadEngineDll() {
    if (g_app.hEngine) {
        FreeLibrary(g_app.hEngine);
        g_app.hEngine = nullptr;
    }
    g_app.AC_Init = nullptr;
    g_app.AC_Shutdown = nullptr;
    g_app.AC_SetSettings = nullptr;
    g_app.AC_Enable = nullptr;
    g_app.AC_Disable = nullptr;
    g_app.AC_IsEnabled = nullptr;
    g_app.AC_GetRunState = nullptr;
}

static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    switch (msg) {
        case WM_CREATE: {
            CreateAllControls(hwnd);

            EnsureDefaultProfileExists();
            std::string lastProfile = LoadLastProfileMeta();
            if (lastProfile.empty() || !ProfileExists(lastProfile)) lastProfile = "Default";

            {
                ClickerSettings loaded;
                if (LoadProfile(lastProfile, loaded)) {
                    std::lock_guard<std::mutex> lock(g_app.settingsMutex);
                    g_app.settings = loaded;
                }
            }

            RefreshProfileCombo(lastProfile);
            PushSettingsToUi();
            RelayoutWindow(hwnd);
            UpdateEnableButtonText();
            UpdateStatusForRunState(RunState::Idle, false);
            g_app.unsavedChanges.store(false);
            return 0;
        }

        case WM_TIMER: {
            if (wParam == ID_TIMER_KEYCAPTURE) {
                EndKeyCapture(hwnd, false, 0);
            }
            return 0;
        }

        case WM_KEYDOWN: {
            if (g_waitingForKeyCapture) {
                UINT vk = (UINT)wParam;
                if (vk == VK_ESCAPE) {
                    EndKeyCapture(hwnd, false, 0);
                } else {
                    EndKeyCapture(hwnd, true, vk);
                }
                return 0;
            }
            break;
        }

        case AC_WM_STATUS_UPDATE: {
            int rs = g_app.AC_GetRunState ? g_app.AC_GetRunState() : (int)RunState::Idle;
            bool en = g_app.AC_IsEnabled ? g_app.AC_IsEnabled() != FALSE : false;
            UpdateStatusForRunState((RunState)rs, en);
            return 0;
        }

        case WM_COMMAND: {
            int id = LOWORD(wParam);
            int notifyCode = HIWORD(wParam);

            if (g_waitingForKeyCapture && id != ID_BUTTON_KEYSELECT) {
                return 0;
            }

            switch (id) {
                case ID_RADIO_SPAM:
                case ID_RADIO_HOLD: {
                    if (notifyCode == BN_CLICKED) {
                        PullUiToSettings();
                        RefreshModeVisibility();
                        RelayoutWindow(hwnd);
                        MarkUnsavedAndDisable(hwnd);
                    }
                    return 0;
                }
                case ID_RADIO_HOLD_IMMEDIATE:
                case ID_RADIO_HOLD_DOUBLECLICK:
                case ID_RADIO_HOLD_WAITKEY: {
                    if (notifyCode == BN_CLICKED) {
                        PullUiToSettings();
                        RefreshModeVisibility();
                        MarkUnsavedAndDisable(hwnd);
                    }
                    return 0;
                }
                case ID_EDIT_SPAM_AUTOCPS:
                case ID_EDIT_SPAM_TRIGGERCPS:
                case ID_EDIT_SPAM_DELAY:
                case ID_EDIT_HOLD_AUTOCPS:
                case ID_EDIT_HOLD_DELAY:
                case ID_EDIT_DOUBLECLICK_INTERVAL:
                case ID_EDIT_ADV_STOPCHECKCOUNT:
                case ID_EDIT_ADV_STOPCHECKWINDOW:
                case ID_EDIT_ADV_TRIGGERWINDOW:
                case ID_EDIT_ADV_TICKMS:
                case ID_EDIT_ADV_MININTERVAL: {
                    if (notifyCode == EN_CHANGE) {
                        PullUiToSettings();
                        MarkUnsavedAndDisable(hwnd);
                    }
                    return 0;
                }
                case ID_BUTTON_KEYSELECT: {
                    if (notifyCode == BN_CLICKED) {
                        BeginKeyCapture(hwnd);
                    }
                    return 0;
                }
                case ID_BUTTON_ADVANCED_TOGGLE: {
                    if (notifyCode == BN_CLICKED) {
                        g_advancedExpanded = !g_advancedExpanded;
                        RefreshModeVisibility();
                        RelayoutWindow(hwnd);
                    }
                    return 0;
                }
                case ID_COMBO_PROFILE: {
                    if (notifyCode == CBN_SELCHANGE) {
                        std::string chosen = GetSelectedProfileName();
                        std::string current;
                        {
                            std::lock_guard<std::mutex> lock(g_app.settingsMutex);
                            current = g_app.settings.profileName;
                        }
                        if (chosen == current) return 0;
                        if (!TryDoProfileSwitch(hwnd, chosen)) {
                            RefreshProfileCombo(current);
                        }
                    }
                    return 0;
                }
                case ID_BUTTON_NEWPROFILE: {
                    if (notifyCode == BN_CLICKED) {
                        std::string newName;
                        if (PromptForProfileName(hwnd, newName)) {
                            if (ProfileExists(newName)) {
                                MessageBoxA(hwnd, "A profile with that name already exists.", "Error", MB_ICONERROR);
                                return 0;
                            }
                            ClickerSettings fresh;
                            fresh.profileName = newName;
                            SaveProfile(fresh);
                            RefreshProfileCombo(newName);
                            LoadProfileIntoAppAndUi(newName);
                        }
                    }
                    return 0;
                }
                case ID_BUTTON_DELETEPROFILE: {
                    if (notifyCode == BN_CLICKED) {
                        std::string chosen = GetSelectedProfileName();
                        if (chosen.empty()) return 0;
                        std::vector<std::string> all = ListProfiles();
                        if (all.size() <= 1) {
                            MessageBoxA(hwnd, "You cannot delete the last remaining profile.", "Error", MB_ICONWARNING);
                            return 0;
                        }
                        std::string confirmMsg = "Delete profile \"" + chosen + "\"? This cannot be undone.";
                        int r = MessageBoxA(hwnd, confirmMsg.c_str(), "Confirm Delete", MB_YESNO | MB_ICONWARNING);
                        if (r == IDYES) {
                            DeleteProfileFile(chosen);
                            std::vector<std::string> remaining = ListProfiles();
                            std::string nextProfile = remaining.empty() ? "Default" : remaining[0];
                            g_app.unsavedChanges.store(false);
                            RefreshProfileCombo(nextProfile);
                            LoadProfileIntoAppAndUi(nextProfile);
                        }
                    }
                    return 0;
                }
                case ID_BUTTON_SAVEPROFILE: {
                    if (notifyCode == BN_CLICKED) {
                        if (DoSaveCurrentProfile()) {
                            bool en = g_app.AC_IsEnabled ? g_app.AC_IsEnabled() != FALSE : false;
                            int rs = g_app.AC_GetRunState ? g_app.AC_GetRunState() : (int)RunState::Idle;
                            UpdateStatusForRunState((RunState)rs, en);
                        } else {
                            MessageBoxA(hwnd, "Failed to save profile.", "Error", MB_ICONERROR);
                        }
                    }
                    return 0;
                }
                case ID_BUTTON_ENABLE: {
                    if (notifyCode == BN_CLICKED) {
                        bool en = g_app.AC_IsEnabled ? g_app.AC_IsEnabled() != FALSE : false;
                        if (en) {
                            DisableProgram();
                        } else {
                            TryEnableProgram(hwnd);
                        }
                    }
                    return 0;
                }
            }
            break;
        }

        case WM_CLOSE: {
            if (g_app.unsavedChanges.load()) {
                std::string currentProfile;
                {
                    std::lock_guard<std::mutex> lock(g_app.settingsMutex);
                    currentProfile = g_app.settings.profileName;
                }
                ConfirmResult r = AskSaveUnsavedChanges(hwnd, currentProfile);
                if (r == ConfirmResult::Cancel) return 0;
                if (r == ConfirmResult::SaveAndProceed) DoSaveCurrentProfile();
            }
            DestroyWindow(hwnd);
            return 0;
        }

        case WM_DESTROY: {
            if (g_app.AC_Shutdown) g_app.AC_Shutdown();
            UnloadEngineDll();
            PostQuitMessage(0);
            return 0;
        }
    }
    return DefWindowProc(hwnd, msg, wParam, lParam);
}

static const char* WND_CLASS_NAME = "AutoClickerMainWindowClass";

static ATOM RegisterMainWindowClass(HINSTANCE hInstance) {
    WNDCLASSEXA wc = {};
    wc.cbSize = sizeof(WNDCLASSEXA);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInstance;
    wc.hIcon = LoadIconA(nullptr, IDI_APPLICATION);
    wc.hCursor = LoadCursorA(nullptr, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_BTNFACE + 1);
    wc.lpszClassName = WND_CLASS_NAME;
    wc.hIconSm = LoadIconA(nullptr, IDI_APPLICATION);
    return RegisterClassExA(&wc);
}

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nCmdShow) {
    (void)hPrevInstance;
    (void)lpCmdLine;

    INITCOMMONCONTROLSEX icc = {};
    icc.dwSize = sizeof(icc);
    icc.dwICC = ICC_STANDARD_CLASSES | ICC_BAR_CLASSES;
    InitCommonControlsEx(&icc);

    InitDataPaths();

    if (!RegisterMainWindowClass(hInstance)) {
        MessageBoxA(nullptr, "Failed to register window class.", "AutoClicker", MB_ICONERROR);
        return 1;
    }

    RECT desiredRect = {0, 0, WIN_WIDTH, WIN_HEIGHT_COLLAPSED};
    AdjustWindowRect(&desiredRect, WS_OVERLAPPEDWINDOW & ~WS_MAXIMIZEBOX & ~WS_THICKFRAME, FALSE);
    int actualWidth = desiredRect.right - desiredRect.left;
    int actualHeight = desiredRect.bottom - desiredRect.top;

    HWND hwnd = CreateWindowExA(
        0,
        WND_CLASS_NAME,
        "AutoClicker",
        (WS_OVERLAPPEDWINDOW & ~WS_MAXIMIZEBOX & ~WS_THICKFRAME) | WS_CLIPCHILDREN,
        CW_USEDEFAULT, CW_USEDEFAULT,
        actualWidth, actualHeight,
        nullptr, nullptr, hInstance, nullptr
    );

    if (!hwnd) {
        MessageBoxA(nullptr, "Failed to create main window.", "AutoClicker", MB_ICONERROR);
        return 1;
    }

    ShowWindow(hwnd, nCmdShow);
    UpdateWindow(hwnd);

    if (!LoadEngineDll()) {
        MessageBoxA(hwnd, "Failed to load ac_engine.dll.\nMake sure ac_engine.dll is in the same folder as AutoClicker.exe.",
            "AutoClicker", MB_ICONERROR);
        DestroyWindow(hwnd);
        return 1;
    }

    if (!g_app.AC_Init(hwnd)) {
        MessageBoxA(hwnd, "Failed to initialize ac_engine.", "AutoClicker", MB_ICONERROR);
        UnloadEngineDll();
        DestroyWindow(hwnd);
        return 1;
    }

    {
        std::lock_guard<std::mutex> lock(g_app.settingsMutex);
        g_app.AC_SetSettings(&g_app.settings);
    }

    MSG msg;
    while (GetMessage(&msg, nullptr, 0, 0) > 0) {
        if (g_waitingForKeyCapture) {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
        } else {
            if (!IsDialogMessage(hwnd, &msg)) {
                TranslateMessage(&msg);
                DispatchMessage(&msg);
            }
        }
    }

    if (g_app.AC_Shutdown) g_app.AC_Shutdown();
    UnloadEngineDll();

    if (g_ui.font) DeleteObject(g_ui.font);

    return (int)msg.wParam;
}

