/**
 * autoclicker.cpp — Cross-platform high-performance auto-clicker DLL
 *
 * Windows: uses SendInput API for hardware-level input injection.
 * Linux:   uses X11/XTest extension.
 *
 * Build:
 *   Windows (MSVC): cl /LD autoclicker.cpp /Fe:autoclicker.dll
 *   Windows (MinGW): g++ -shared -o autoclicker.dll autoclicker.cpp -static-libgcc -static-libstdc++
 *   Linux:  g++ -fPIC -shared -o libautoclicker.so autoclicker.cpp -lX11 -lXtst -lpthread
 */

#include "autoclicker.h"

/* ================================================================== */
/*  Platform-specific includes & helpers                               */
/* ================================================================== */

#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <cmath>

#ifdef _WIN32
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
    #pragma comment(lib, "user32.lib")
#else
    #include <X11/Xlib.h>
    #include <X11/extensions/XTest.h>
    #include <pthread.h>
    #include <unistd.h>
#endif

/* ================================================================== */
/*  Internal state                                                     */
/* ================================================================== */

static struct {
    ClickerConfig  config;
    ClickerInputState input;

    volatile int  running;      /* atomic flag, 1 = thread should run */
    volatile int  thread_alive; /* 1 after thread starts, 0 after joins */

#ifdef _WIN32
    HANDLE        thread_handle;
    CRITICAL_SECTION lock;
#else
    pthread_t     thread_id;
    pthread_mutex_t mutex;
#endif
} g_state;

/* ── Forward declarations ─────────────────────────────────────────── */
static void    clicker_thread_func(void);
static void    inject_mouse_click(void);
static double  get_monotonic_seconds(void);
static void    sleep_ms(unsigned int ms);
static void    lock(void);
static void    unlock(void);

/* ================================================================== */
/*  Public API                                                         */
/* ================================================================== */

EXPORT int Clicker_Init(const ClickerConfig* config)
{
    if (!config) return -1;

    memset(&g_state, 0, sizeof(g_state));

#ifdef _WIN32
    InitializeCriticalSection(&g_state.lock);
#else
    pthread_mutex_init(&g_state.mutex, NULL);
#endif

    g_state.config = *config;
    g_state.running = 0;
    g_state.thread_alive = 0;
    return 0;
}

EXPORT void Clicker_SetInputState(const ClickerInputState* state)
{
    if (!state) return;
    lock();
    g_state.input = *state;
    unlock();
}

EXPORT void Clicker_Start(void)
{
    lock();
    if (g_state.thread_alive) {
        unlock();
        return;  /* already running */
    }
    g_state.running = 1;

#ifdef _WIN32
    g_state.thread_handle = CreateThread(
        NULL, 0,
        (LPTHREAD_START_ROUTINE)clicker_thread_func,
        NULL, 0, NULL
    );
    if (g_state.thread_handle) {
        g_state.thread_alive = 1;
    }
#else
    if (pthread_create(&g_state.thread_id, NULL,
                       (void* (*)(void*))clicker_thread_func,
                       NULL) == 0) {
        g_state.thread_alive = 1;
    }
#endif
    unlock();
}

EXPORT void Clicker_Stop(void)
{
    lock();
    if (!g_state.thread_alive) {
        unlock();
        return;
    }
    g_state.running = 0;
#ifdef _WIN32
    HANDLE h = g_state.thread_handle;
    unlock();
    WaitForSingleObject(h, 3000);
    CloseHandle(h);
#else
    pthread_t tid = g_state.thread_id;
    unlock();
    pthread_join(tid, NULL);
#endif
    lock();
    g_state.thread_alive = 0;
    unlock();
}

EXPORT int Clicker_IsRunning(void)
{
    return g_state.running;
}

EXPORT void Clicker_UpdateConfig(const ClickerConfig* config)
{
    if (!config) return;
    lock();
    g_state.config = *config;
    unlock();
}

EXPORT void Clicker_Destroy(void)
{
    Clicker_Stop();
    lock();
    memset(&g_state, 0, sizeof(g_state));
#ifdef _WIN32
    DeleteCriticalSection(&g_state.lock);
#else
    pthread_mutex_destroy(&g_state.mutex);
#endif
    unlock();
}

/* ================================================================== */
/*  Internal helpers                                                   */
/* ================================================================== */

static void lock(void)
{
#ifdef _WIN32
    EnterCriticalSection(&g_state.lock);
#else
    pthread_mutex_lock(&g_state.mutex);
#endif
}

static void unlock(void)
{
#ifdef _WIN32
    LeaveCriticalSection(&g_state.lock);
#else
    pthread_mutex_unlock(&g_state.mutex);
#endif
}

static double get_monotonic_seconds(void)
{
#ifdef _WIN32
    LARGE_INTEGER freq, count;
    QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&count);
    return (double)count.QuadPart / (double)freq.QuadPart;
#else
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (double)ts.tv_sec + (double)ts.tv_nsec / 1.0e9;
#endif
}

static void sleep_ms(unsigned int ms)
{
    if (ms == 0) return;
#ifdef _WIN32
    Sleep(ms);
#else
    usleep(ms * 1000);
#endif
}

/* ================================================================== */
/*  Click injection — platform-specific                                */
/* ================================================================== */

#ifdef _WIN32
/* ── Windows: SendInput ───────────────────────────────────────────── */
static void inject_mouse_click(void)
{
    INPUT inputs[2] = {0};

    /* Mouse down */
    inputs[0].type = INPUT_MOUSE;
    inputs[0].mi.dwFlags = MOUSEEVENTF_LEFTDOWN;

    /* Mouse up */
    inputs[1].type = INPUT_MOUSE;
    inputs[1].mi.dwFlags = MOUSEEVENTF_LEFTUP;

    SendInput(2, inputs, sizeof(INPUT));
}
#else
/* ── Linux: X11 / XTest ──────────────────────────────────────────── */
static Display* get_xdisplay(void)
{
    /* XOpenDisplay is not thread-safe; we open on first use.
       In practice this is called from the clicker thread only. */
    static Display* dpy = NULL;
    static int tried = 0;
    if (!dpy && !tried) {
        tried = 1;
        dpy = XOpenDisplay(NULL);
    }
    return dpy;
}

static void inject_mouse_click(void)
{
    Display* dpy = get_xdisplay();
    if (!dpy) return;

    int button = Button1; /* left mouse button */

    XTestFakeButtonEvent(dpy, button, True,   CurrentTime);
    XTestFakeButtonEvent(dpy, button, False,  CurrentTime);
    XFlush(dpy);
}
#endif

/* ================================================================== */
/*  Main clicker thread                                                */
/* ================================================================== */

static void clicker_thread_func(void)
{
    double next_tick = 0.0;
    int    is_hold_mode = 0;
    int    hold_activation = 0;
    int    hold_delay_ms = 0;
    int    dbl_interval_ms = 0;
    double cps_delay = 0.0;
    int    prev_left = 0;    /* edge detection for double-click */

    /* ── Loop until told to stop ────────────────────────────────────── */
    while (g_state.running) {

        /* Snapshot config under lock */
        lock();
        ClickerConfig  cfg  = g_state.config;
        ClickerInputState inp = g_state.input;
        unlock();

        is_hold_mode   = (cfg.mode == CLICKER_MODE_HOLD);
        hold_activation = cfg.hold_activation;
        hold_delay_ms   = cfg.hold_delay;
        dbl_interval_ms = cfg.dbl_interval;
        cps_delay       = (cfg.trigger_cps > 0) ? (1.0 / (double)cfg.trigger_cps) : 0.1;
        int wait_enabled      = cfg.wait_enabled;
        int left_down         = inp.left_button_down;
        int wait_key_down     = inp.wait_key_down;
        int right_down        = inp.right_button_down;
        (void)right_down; /* reserved */

        /* ── Check wait-for-key gating ──────────────────────────────── */
        int should_click = 0;
        if (!wait_enabled) {
            should_click = left_down;
        } else {
            /* Wait mode: only click if the designated button/key is held */
            should_click = left_down && wait_key_down;
        }

        /* ── HOLD mode ─────────────────────────────────────────────── */
        if (is_hold_mode) {
            if (hold_activation == CLICKER_HOLD_DOUBLECLICK) {
                /* ── Double-click hold mode ─────────────────────────── */
                /* Fire double-click on rising edge of left button */
                if (left_down && !prev_left) {
                    /* First click */
                    inject_mouse_click();
                    /* Short delay between clicks */
                    if (dbl_interval_ms > 0) {
                        sleep_ms(dbl_interval_ms);
                    } else {
                        sleep_ms(50); /* default 50ms */
                    }
                    /* Second click */
                    if (g_state.running && left_down) {
                        inject_mouse_click();
                    }
                }
                prev_left = left_down;
            } else {
                /* ── Normal hold mode ────────────────────────────────── */
                /* Click repeatedly at hold_delay_ms interval */
                if (should_click) {
                    double now = get_monotonic_seconds();
                    if (next_tick == 0.0) {
                        next_tick = now + (hold_delay_ms / 1000.0);
                    } else if (now >= next_tick) {
                        inject_mouse_click();
                        /* Advance by hold_delay_ms, but don't fall behind */
                        next_tick += (hold_delay_ms / 1000.0);
                        if (next_tick < now) {
                            next_tick = now + (hold_delay_ms / 1000.0);
                        }
                    }
                } else {
                    next_tick = 0.0; /* reset timer when not held */
                }
            }
            /* Small sleep to avoid busy-wait in hold mode */
            sleep_ms(2);
            continue;
        }

        /* ── SPAM mode ─────────────────────────────────────────────── */
        if (should_click) {
            double now = get_monotonic_seconds();
            if (next_tick == 0.0) {
                next_tick = now + cps_delay;
                inject_mouse_click();
            } else if (now >= next_tick) {
                inject_mouse_click();
                next_tick += cps_delay;
                if (next_tick < now) {
                    next_tick = now + cps_delay;
                }
            }
        } else {
            next_tick = 0.0;
        }

        /* ── Micro-sleep to keep CPU usage low (~2% on modern CPUs) ─ */
        sleep_ms(1);
    }

    /* Thread ending — reset display pointer on Linux to avoid leaks */
#ifndef _WIN32
    Display* dpy = get_xdisplay();
    if (dpy) XCloseDisplay(dpy);
#endif
}

