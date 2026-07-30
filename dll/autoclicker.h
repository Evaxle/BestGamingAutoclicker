#ifndef AUTOCLICKER_H
#define AUTOCLICKER_H

#ifdef _WIN32
    #define EXPORT __declspec(dllexport)
#else
    #define EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* ── Mode Constants ──────────────────────────────────────────────── */
#define CLICKER_MODE_SPAM  0
#define CLICKER_MODE_HOLD  1

#define CLICKER_HOLD_NORMAL       0
#define CLICKER_HOLD_DOUBLECLICK  1

/* ── Config struct passed from Python ─────────────────────────────── */
typedef struct {
    int  trigger_cps;          /* spam mode: clicks per second (1-200)   */
    int  turbo_cps;            /* turbo cps (reserved, 1-500)           */
    int  stop_delay;           /* ms delay before auto-stop (0-10000)   */
    int  hold_delay;           /* hold mode: ms between clicks (0-10000)*/
    int  dbl_interval;         /* double-click: ms between two clicks   */
    int  mode;                 /* CLICKER_MODE_SPAM or CLICKER_MODE_HOLD */
    int  hold_activation;      /* CLICKER_HOLD_NORMAL or _DOUBLECLICK    */
    int  wait_enabled;         /* whether to gate clicks on wait_button */
    char wait_button[32];      /* key name: "lbutton","rbutton","f",etc.*/
} ClickerConfig;

/* ── Input state pushed from Python listeners ─────────────────────── */
typedef struct {
    int left_button_down;      /* 1 = LMB is currently held             */
    int right_button_down;     /* 1 = RMB is currently held             */
    int wait_key_down;         /* 1 = the wait key is currently pressed */
} ClickerInputState;

/* ── Public API ───────────────────────────────────────────────────── */

/**
 * Initialize the clicker engine with a configuration.
 * Must be called before Start().
 * Returns 0 on success, non-zero on failure.
 */
EXPORT int  Clicker_Init(const ClickerConfig* config);

/**
 * Push the latest input state (mouse buttons / wait-key) to the engine.
 * Call this from your input listener callbacks for lowest latency.
 */
EXPORT void Clicker_SetInputState(const ClickerInputState* state);

/**
 * Start the auto-clicking thread. The thread runs until Stop() is called
 * or the DLL is unloaded.
 */
EXPORT void Clicker_Start(void);

/**
 * Stop the auto-clicking thread. Blocks until the thread finishes.
 * Safe to call even if the clicker is not running.
 */
EXPORT void Clicker_Stop(void);

/**
 * Returns non-zero if the clicker thread is currently running.
 */
EXPORT int  Clicker_IsRunning(void);

/**
 * Update configuration at runtime without restarting.
 * The clicker will pick up new values on the next iteration.
 */
EXPORT void Clicker_UpdateConfig(const ClickerConfig* config);

/**
 * Release all resources, stop the thread (if running).
 * After calling Destroy you must call Init again before Start.
 */
EXPORT void Clicker_Destroy(void);

#ifdef __cplusplus
}
#endif

#endif /* AUTOCLICKER_H */

