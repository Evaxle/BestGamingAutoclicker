#include <windows.h>

// Module handle of ac_engine.dll itself, used when installing the low-level
// mouse/keyboard hooks (the hook procedures live inside this DLL).
HMODULE g_acEngineModule = nullptr;

BOOL APIENTRY DllMain(HMODULE hModule,
                      DWORD  ul_reason_for_call,
                      LPVOID lpReserved) {
    (void)lpReserved;
    switch (ul_reason_for_call) {
    case DLL_PROCESS_ATTACH:
        g_acEngineModule = hModule;
        DisableThreadLibraryCalls(hModule);
        break;
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

