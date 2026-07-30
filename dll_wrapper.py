"""
dll_wrapper.py — Python CTypes wrapper for the autoclicker shared library.

Provides a clean Pythonic interface to the C++ DLL (.dll / .so).
Handles platform detection and library loading automatically.
"""

from __future__ import annotations

import os
import sys
import threading
from ctypes import (
    CDLL,
    Structure,
    c_char,
    c_double,
    c_int,
    byref,
    sizeof,
)
from pathlib import Path
from typing import Optional


# ── Determine library path ────────────────────────────────────────────
def _get_library_path() -> Path:
    """Return the path to the compiled shared library."""
    base = Path(__file__).resolve().parent / "dll"
    if sys.platform == "win32":
        return base / "autoclicker.dll"
    else:
        return base / "libautoclicker.so"


# ── CTypes struct definitions ─────────────────────────────────────────
class ClickerConfig(Structure):
    _fields_ = [
        ("trigger_cps", c_int),
        ("turbo_cps", c_int),
        ("stop_delay", c_int),
        ("hold_delay", c_int),
        ("dbl_interval", c_int),
        ("mode", c_int),
        ("hold_activation", c_int),
        ("wait_enabled", c_int),
        ("wait_button", c_char * 32),
    ]


class ClickerInputState(Structure):
    _fields_ = [
        ("left_button_down", c_int),
        ("right_button_down", c_int),
        ("wait_key_down", c_int),
    ]


# ── Mode constants (must match autoclicker.h) ─────────────────────────
CLICKER_MODE_SPAM = 0
CLICKER_MODE_HOLD = 1

CLICKER_HOLD_NORMAL = 0
CLICKER_HOLD_DOUBLECLICK = 1


# ── DLL Wrapper ───────────────────────────────────────────────────────
class AutoClickerDLL:
    """Python wrapper around the native autoclicker C++ DLL."""

    def __init__(self) -> None:
        self._lib: Optional[CDLL] = None
        self._loaded = False
        self._load_error: Optional[str] = None

    # ── Library loading ───────────────────────────────────────────────

    def load(self) -> bool:
        """Load the native library. Returns True on success."""
        if self._loaded:
            return True

        lib_path = _get_library_path()
        if not lib_path.exists():
            self._load_error = (
                f"Library not found: {lib_path}\n"
                f"Run the build script for your platform:\n"
                f"  Linux:  cd dll && make\n"
                f"  Windows: cd dll && build_win.bat"
            )
            return False

        try:
            self._lib = CDLL(str(lib_path))
        except OSError as exc:
            self._load_error = f"Failed to load library: {exc}"
            return False

        self._lib.Clicker_Init.argtypes = [ClickerConfig]
        self._lib.Clicker_Init.restype = c_int

        self._lib.Clicker_SetInputState.argtypes = [ClickerInputState]
        self._lib.Clicker_SetInputState.restype = None

        self._lib.Clicker_Start.argtypes = []
        self._lib.Clicker_Start.restype = None

        self._lib.Clicker_Stop.argtypes = []
        self._lib.Clicker_Stop.restype = None

        self._lib.Clicker_IsRunning.argtypes = []
        self._lib.Clicker_IsRunning.restype = c_int

        self._lib.Clicker_UpdateConfig.argtypes = [ClickerConfig]
        self._lib.Clicker_UpdateConfig.restype = None

        self._lib.Clicker_Destroy.argtypes = []
        self._lib.Clicker_Destroy.restype = None

        self._loaded = True
        return True

    def is_loaded(self) -> bool:
        return self._loaded

    def get_load_error(self) -> Optional[str]:
        return self._load_error

    # ── Input state helper ────────────────────────────────────────────

    @staticmethod
    def _build_input_state(
        left_down: bool, right_down: bool, wait_key_down: bool
    ) -> ClickerInputState:
        return ClickerInputState(
            left_button_down=1 if left_down else 0,
            right_button_down=1 if right_down else 0,
            wait_key_down=1 if wait_key_down else 0,
        )

    @staticmethod
    def _build_config(
        trigger_cps: int,
        turbo_cps: int,
        stop_delay: int,
        hold_delay: int,
        dbl_interval: int,
        mode: str,
        hold_activation: str,
        wait_enabled: bool,
        wait_button: str,
    ) -> ClickerConfig:
        mode_val = CLICKER_MODE_SPAM if mode == "spam" else CLICKER_MODE_HOLD
        hold_val = (
            CLICKER_HOLD_DOUBLECLICK
            if hold_activation == "double-click"
            else CLICKER_HOLD_NORMAL
        )
        wait_btn_bytes = wait_button.encode("utf-8")[:31] + b"\0" if wait_button else b"\0"

        return ClickerConfig(
            trigger_cps=trigger_cps,
            turbo_cps=turbo_cps,
            stop_delay=stop_delay,
            hold_delay=hold_delay,
            dbl_interval=dbl_interval,
            mode=mode_val,
            hold_activation=hold_val,
            wait_enabled=1 if wait_enabled else 0,
            wait_button=wait_btn_bytes,
        )

    # ── Public API ────────────────────────────────────────────────────

    def init(self, config: ClickerConfig) -> bool:
        """Initialize the clicker engine. Returns True on success."""
        if not self._loaded:
            return False
        result = self._lib.Clicker_Init(byref(config))
        return result == 0

    def init_from_app_config(
        self,
        trigger_cps: int,
        turbo_cps: int,
        stop_delay: int,
        hold_delay: int,
        dbl_interval: int,
        mode: str,
        hold_activation: str,
        wait_enabled: bool,
        wait_button: str,
    ) -> bool:
        """Convenience: build config from Python values and init."""
        config = self._build_config(
            trigger_cps=trigger_cps,
            turbo_cps=turbo_cps,
            stop_delay=stop_delay,
            hold_delay=hold_delay,
            dbl_interval=dbl_interval,
            mode=mode,
            hold_activation=hold_activation,
            wait_enabled=wait_enabled,
            wait_button=wait_button,
        )
        return self.init(config)

    def set_input_state(
        self, left_down: bool = False, right_down: bool = False,
        wait_key_down: bool = False
    ) -> None:
        """Push the current mouse/keyboard state to the DLL."""
        if not self._loaded:
            return
        state = self._build_input_state(left_down, right_down, wait_key_down)
        self._lib.Clicker_SetInputState(byref(state))

    def start(self) -> None:
        """Start the clicker thread in the DLL."""
        if not self._loaded:
            return
        self._lib.Clicker_Start()

    def stop(self) -> None:
        """Stop the clicker thread."""
        if not self._loaded:
            return
        self._lib.Clicker_Stop()

    def is_running(self) -> bool:
        """Check if the clicker thread is active."""
        if not self._loaded:
            return False
        return self._lib.Clicker_IsRunning() != 0

    def update_config(self, config: ClickerConfig) -> None:
        """Update configuration at runtime."""
        if not self._loaded:
            return
        self._lib.Clicker_UpdateConfig(byref(config))

    def update_config_from_app(
        self,
        trigger_cps: int,
        turbo_cps: int,
        stop_delay: int,
        hold_delay: int,
        dbl_interval: int,
        mode: str,
        hold_activation: str,
        wait_enabled: bool,
        wait_button: str,
    ) -> None:
        """Convenience: update config from Python values."""
        config = self._build_config(
            trigger_cps=trigger_cps,
            turbo_cps=turbo_cps,
            stop_delay=stop_delay,
            hold_delay=hold_delay,
            dbl_interval=dbl_interval,
            mode=mode,
            hold_activation=hold_activation,
            wait_enabled=wait_enabled,
            wait_button=wait_button,
        )
        self.update_config(config)

    def destroy(self) -> None:
        """Release all resources (stops thread, frees memory)."""
        if not self._loaded:
            return
        self._lib.Clicker_Destroy()


# ── Singleton instance ────────────────────────────────────────────────
# Module-level instance for easy import.
_clicker = AutoClickerDLL()


def get_clicker() -> AutoClickerDLL:
    """Get the singleton DLL wrapper instance."""
    return _clicker


def try_load_clicker() -> bool:
    """Attempt to load the native library. Returns True if successful."""
    return _clicker.load()

