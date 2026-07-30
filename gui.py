from __future__ import annotations

import os
import sys
import threading
import time
import tkinter as tk
from pathlib import Path
from tkinter import messagebox, simpledialog


def _is_headless_environment() -> bool:
    if os.name == "nt":
        return False
    return not os.environ.get("DISPLAY")


import customtkinter as ctk

try:
    from pynput import keyboard as pynput_keyboard, mouse as pynput_mouse
except Exception:  # pragma: no cover - headless fallback
    pynput_keyboard = None
    pynput_mouse = None

from autoclicker_config import (
    AppConfig,
    list_profiles,
    load_config,
    load_runtime_state,
    profile_path,
    save_config,
    save_runtime_state,
    sanitize_config,
    validate_settings,
)

from dll_wrapper import (
    AutoClickerDLL,
    get_clicker,
    try_load_clicker,
)

# ── Purple color palette ──────────────────────────────────────────────
PURPLE_PRIMARY = "#7c3aed"
PURPLE_HOVER = "#6d28d9"
PURPLE_LIGHT = "#a78bfa"
PURPLE_DARK = "#4c1d95"
PURPLE_BG = "#1e1b2e"
PURPLE_FRAME = "#2a2640"
PURPLE_ACTIVE = "#9333ea"
GREEN_ENABLE = "#22c55e"
RED_DISABLE = "#ef4444"

# ── Log Level Colors for Console ─────────────────────────────────────
LOG_COLORS = {
    "INFO": "#9ca3af",      # grey
    "SUCCESS": "#22c55e",   # green
    "WARNING": "#fbbf24",   # yellow
    "ERROR": "#ef4444",     # red
    "FILE": "#a78bfa",      # purple
    "CLICKER": "#22d3ee",   # cyan
}


class ConsoleLog:
    """A color-coded, scrolling console widget for displaying debug info."""

    def __init__(self, parent: tk.Widget) -> None:
        self._frame = ctk.CTkFrame(parent, corner_radius=10, fg_color="#1a1a2e")
        self._frame.grid_columnconfigure(0, weight=1)
        self._frame.grid_rowconfigure(0, weight=1)

        # Header row
        header = ctk.CTkFrame(self._frame, fg_color="transparent")
        header.grid(row=0, column=0, sticky="ew", padx=8, pady=(6, 2))
        header.grid_columnconfigure(0, weight=1)

        ctk.CTkLabel(
            header,
            text="📋 Console Log",
            font=("Segoe UI", 13, "bold"),
            text_color=PURPLE_LIGHT,
        ).grid(row=0, column=0, sticky="w")

        clear_btn = ctk.CTkButton(
            header,
            text="Clear",
            width=70,
            height=24,
            font=("Segoe UI", 11),
            fg_color=PURPLE_DARK,
            hover_color=PURPLE_PRIMARY,
            command=self._clear,
        )
        clear_btn.grid(row=0, column=1, sticky="e", padx=(8, 0))

        # Text widget for log output
        self._text = tk.Text(
            self._frame,
            state="disabled",
            wrap="word",
            bg="#12121e",
            fg="#c0c0c0",
            insertbackground="white",
            font=("Consolas", 10),
            borderwidth=0,
            relief="flat",
            height=10,
            padx=8,
            pady=6,
        )
        self._text.grid(row=1, column=0, sticky="nsew", padx=0, pady=(2, 0))

        # Scrollbar
        scrollbar = ctk.CTkScrollbar(self._frame, command=self._text.yview)
        scrollbar.grid(row=1, column=1, sticky="ns", pady=(2, 0))
        self._text.configure(yscrollcommand=scrollbar.set)

        # Configure color tags
        for level, color in LOG_COLORS.items():
            self._text.tag_config(level.lower(), foreground=color)

        self._text.tag_config("bold", font=("Consolas", 10, "bold"))

    def _clear(self) -> None:
        self._text.configure(state="normal")
        self._text.delete("1.0", "end")
        self._text.configure(state="disabled")

    def write(self, message: str, level: str = "INFO") -> None:
        """Append a timestamped, color-coded message to the console."""
        from datetime import datetime

        timestamp = datetime.now().strftime("%H:%M:%S")
        tag = level.lower() if level.lower() in LOG_COLORS else "info"
        line = f"[{timestamp}] [{level}] {message}\n"

        self._text.configure(state="normal")
        self._text.insert("end", f"[{timestamp}] ", "info")
        self._text.insert("end", f"[{level}] ", tag)
        self._text.insert("end", f"{message}\n", tag)
        self._text.see("end")  # auto-scroll
        self._text.configure(state="disabled")

    def grid(self, **kwargs):
        self._frame.grid(**kwargs)

    def grid_remove(self):
        self._frame.grid_remove()


class AutoClickerWindow(ctk.CTk):
    def __init__(self) -> None:
        if _is_headless_environment():
            raise tk.TclError(
                "No display available; start the GUI on a desktop session or set DISPLAY."
            )
        super().__init__()
        ctk.set_appearance_mode("dark")
        # Force purple theme by using our own colors throughout
        try:
            ctk.set_default_color_theme("dark-blue")
        except Exception:
            pass

        self.title("Best Gaming AutoClicker")
        self.geometry("940x820")
        self.minsize(860, 720)

        self.data_dir = Path(__file__).resolve().parent / "data"
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.current_profile = "default"
        self.config = sanitize_config(AppConfig(profile_name=self.current_profile))
        self._ensure_default_profile_data()
        self.runtime_state = load_runtime_state(self.data_dir)

# ── Internal state ─────────────────────────────────────────────
        self._mode_value = "spam"
        self._hold_mode_value = "normal"
        self._clicking_enabled = False        # whether DLL is started
        self._left_button_down = False
        self._right_button_down = False
        self._pressed_keys: set[str] = set()
        self._unsaved_changes = False

        # ── Python fallback clicker state (used when DLL unavailable) ─
        self._clicking_event = threading.Event()
        self._clicking_event.clear()
        self._click_thread: threading.Thread | None = None
        self._left_was_down = False
        self._hold_since: float | None = None
        self._double_click_armed = False
        self._idle_timeout = 60.0

# ── Native DLL Clicker Engine ──────────────────────────────────
        self._dll_clicker: AutoClickerDLL = get_clicker()
        self._dll_loaded = try_load_clicker()
        self._dll_load_message = ""
        if self._dll_loaded:
            self._dll_load_message = "Native clicker DLL loaded successfully"
        else:
            self._dll_load_message = (
                f"Native DLL not available: {self._dll_clicker.get_load_error()}. "
                "Falling back to Python pynput-based clicker."
            )

        self._mouse_listener = None
        self._keyboard_listener = None
        self._unsaved_changes = False

        self._build_ui()
        if self._dll_load_message:
            level = "SUCCESS" if self._dll_loaded else "WARNING"
            self.console_log.write(self._dll_load_message, level)
        self._load_profiles()
        self._apply_config_to_form()
        self._apply_runtime_state()
        self._start_input_listeners()
        self._update_unsaved_indicator()

    # ── UI Construction ───────────────────────────────────────────────

    def _build_ui(self) -> None:
        # Root grid
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(0, weight=1)

        # Main container – purple-tinted background
        self.main_frame = ctk.CTkFrame(
            self, corner_radius=16, fg_color=PURPLE_BG
        )
        self.main_frame.grid(row=0, column=0, sticky="nsew", padx=16, pady=16)
        self.main_frame.grid_columnconfigure(0, weight=1)
        # Make rows stretch: title area, content area, bottom bar
        self.main_frame.grid_rowconfigure(2, weight=1)

        # ── Header ─────────────────────────────────────────────────────
        header_frame = ctk.CTkFrame(self.main_frame, fg_color="transparent")
        header_frame.grid(row=0, column=0, sticky="ew", pady=(6, 2))
        header_frame.grid_columnconfigure(0, weight=1)

        title = ctk.CTkLabel(
            header_frame,
            text="⚡ Best Gaming AutoClicker",
            font=("Segoe UI", 26, "bold"),
            text_color=PURPLE_LIGHT,
        )
        title.grid(row=0, column=0, sticky="w")

        subtitle = ctk.CTkLabel(
            header_frame,
            text="Profile-based autoclicking | Purple Edition",
            text_color="#9ca3af",
            font=("Segoe UI", 12),
        )
        subtitle.grid(row=1, column=0, sticky="w", pady=(0, 6))

        # ── Unsaved changes indicator ──────────────────────────────────
        self.unsaved_var = tk.StringVar(value="")
        self.unsaved_label = ctk.CTkLabel(
            header_frame,
            textvariable=self.unsaved_var,
            font=("Segoe UI", 11, "bold"),
            text_color="#fbbf24",
        )
        self.unsaved_label.grid(row=0, column=1, rowspan=2, sticky="e", padx=(8, 4))

        # ── Scrollable content area ────────────────────────────────────
        self.content_canvas = ctk.CTkScrollableFrame(
            self.main_frame, corner_radius=12, fg_color=PURPLE_FRAME
        )
        self.content_canvas.grid(row=2, column=0, sticky="nsew", pady=(4, 8))
        self.content_canvas.grid_columnconfigure(0, weight=1)

        # ── 1. PROFILE SECTION ─────────────────────────────────────────
        self.profile_frame = ctk.CTkFrame(
            self.content_canvas, corner_radius=14, fg_color="#2a2640"
        )
        self.profile_frame.grid(
            row=0, column=0, sticky="ew", padx=6, pady=(8, 12)
        )
        self.profile_frame.grid_columnconfigure(1, weight=1)

        prof_icon = ctk.CTkLabel(
            self.profile_frame, text="📁", font=("Segoe UI", 16)
        )
        prof_icon.grid(row=0, column=0, sticky="w", padx=(14, 4), pady=(10, 2))

        ctk.CTkLabel(
            self.profile_frame,
            text="Profiles",
            font=("Segoe UI", 16, "bold"),
            text_color=PURPLE_LIGHT,
        ).grid(row=0, column=1, sticky="w", pady=(10, 2))

        # Profile selector row
        selector_row = ctk.CTkFrame(self.profile_frame, fg_color="transparent")
        selector_row.grid(row=1, column=0, columnspan=3, sticky="ew", padx=14, pady=(4, 4))
        selector_row.grid_columnconfigure(0, weight=1)

        self.profile_selector = ctk.CTkComboBox(
            selector_row, values=["default"], state="readonly",
            fg_color="#1e1b2e", border_color=PURPLE_PRIMARY,
            button_color=PURPLE_PRIMARY, button_hover_color=PURPLE_HOVER,
            dropdown_fg_color="#2a2640", dropdown_hover_color=PURPLE_HOVER,
        )
        self.profile_selector.grid(row=0, column=0, sticky="ew", padx=(0, 10))
        self.profile_selector.bind(
            "<<ComboboxSelected>>",
            lambda _event: self._on_profile_select(self.profile_selector.get()),
        )

        # Profile action buttons
        self.btn_new_prof = ctk.CTkButton(
            selector_row, text="＋ New", width=90,
            fg_color=PURPLE_PRIMARY, hover_color=PURPLE_HOVER,
            command=self._create_profile,
        )
        self.btn_new_prof.grid(row=0, column=1, padx=(0, 6))

        self.btn_del_prof = ctk.CTkButton(
            selector_row, text="✕ Delete", width=90,
            fg_color="#4a1c1c", hover_color="#6b2a2a",
            text_color="#fca5a5",
            command=self._delete_profile,
        )
        self.btn_del_prof.grid(row=0, column=2)

        # Profile info / last saved
        self.profile_info_var = tk.StringVar(value="")
        self.profile_info_label = ctk.CTkLabel(
            self.profile_frame,
            textvariable=self.profile_info_var,
            font=("Segoe UI", 10),
            text_color="#9ca3af",
        )
        self.profile_info_label.grid(
            row=2, column=0, columnspan=3, sticky="w", padx=14, pady=(2, 10)
        )

        # ── 2. MODE & SETTINGS SECTION ─────────────────────────────────
        self.settings_frame = ctk.CTkFrame(
            self.content_canvas, corner_radius=14, fg_color="#2a2640"
        )
        self.settings_frame.grid(
            row=1, column=0, sticky="ew", padx=6, pady=(4, 12)
        )
        self.settings_frame.grid_columnconfigure(1, weight=1)
        self.settings_frame.grid_columnconfigure(3, weight=1)

        set_icon = ctk.CTkLabel(
            self.settings_frame, text="⚙️", font=("Segoe UI", 16)
        )
        set_icon.grid(row=0, column=0, sticky="w", padx=(14, 4), pady=(10, 2))
        ctk.CTkLabel(
            self.settings_frame,
            text="Mode & Settings",
            font=("Segoe UI", 16, "bold"),
            text_color=PURPLE_LIGHT,
        ).grid(row=0, column=1, columnspan=3, sticky="w", pady=(10, 8))

        # Row: Mode selector
        ctk.CTkLabel(
            self.settings_frame, text="Mode:", font=("Segoe UI", 12)
        ).grid(row=1, column=0, sticky="w", padx=14, pady=8)
        self.mode_frame = ctk.CTkFrame(self.settings_frame, fg_color="transparent")
        self.mode_frame.grid(row=1, column=1, columnspan=3, sticky="w", pady=8)
        self.mode_spam_button = ctk.CTkButton(
            self.mode_frame, text="Spam", width=100,
            command=lambda: self._on_edit(lambda: self._set_mode_selection("spam")),
        )
        self.mode_spam_button.grid(row=0, column=0, padx=(0, 8))
        self.mode_hold_button = ctk.CTkButton(
            self.mode_frame, text="Hold", width=100,
            command=lambda: self._on_edit(lambda: self._set_mode_selection("hold")),
        )
        self.mode_hold_button.grid(row=0, column=1)

        # Row: Trigger CPS / Turbo CPS
        ctk.CTkLabel(
            self.settings_frame, text="Trigger CPS:", font=("Segoe UI", 12)
        ).grid(row=2, column=0, sticky="w", padx=14, pady=8)
        self.trigger_spin = ctk.CTkEntry(
            self.settings_frame, width=140,
            fg_color="#1e1b2e", border_color=PURPLE_DARK,
        )
        self.trigger_spin.grid(row=2, column=1, sticky="w", pady=8)
        self.trigger_spin.bind("<KeyRelease>", lambda e: self._on_edit())

        ctk.CTkLabel(
            self.settings_frame, text="Turbo CPS:", font=("Segoe UI", 12)
        ).grid(row=2, column=2, sticky="w", padx=(14, 4), pady=8)
        self.turbo_spin = ctk.CTkEntry(
            self.settings_frame, width=140,
            fg_color="#1e1b2e", border_color=PURPLE_DARK,
        )
        self.turbo_spin.grid(row=2, column=3, sticky="w", pady=8)
        self.turbo_spin.bind("<KeyRelease>", lambda e: self._on_edit())

        # Row: Stop Delay / Hold Delay
        ctk.CTkLabel(
            self.settings_frame, text="Stop Delay (ms):", font=("Segoe UI", 12)
        ).grid(row=3, column=0, sticky="w", padx=14, pady=8)
        self.stop_spin = ctk.CTkEntry(
            self.settings_frame, width=140,
            fg_color="#1e1b2e", border_color=PURPLE_DARK,
        )
        self.stop_spin.grid(row=3, column=1, sticky="w", pady=8)
        self.stop_spin.bind("<KeyRelease>", lambda e: self._on_edit())

        ctk.CTkLabel(
            self.settings_frame, text="Hold Delay (ms):", font=("Segoe UI", 12)
        ).grid(row=3, column=2, sticky="w", padx=(14, 4), pady=8)
        self.hold_spin = ctk.CTkEntry(
            self.settings_frame, width=140,
            fg_color="#1e1b2e", border_color=PURPLE_DARK,
        )
        self.hold_spin.grid(row=3, column=3, sticky="w", pady=8)
        self.hold_spin.bind("<KeyRelease>", lambda e: self._on_edit())

        # Row: Double Interval / Hold Mode
        ctk.CTkLabel(
            self.settings_frame, text="Double Interval (ms):", font=("Segoe UI", 12)
        ).grid(row=4, column=0, sticky="w", padx=14, pady=8)
        self.dbl_spin = ctk.CTkEntry(
            self.settings_frame, width=140,
            fg_color="#1e1b2e", border_color=PURPLE_DARK,
        )
        self.dbl_spin.grid(row=4, column=1, sticky="w", pady=8)
        self.dbl_spin.bind("<KeyRelease>", lambda e: self._on_edit())

        ctk.CTkLabel(
            self.settings_frame, text="Hold Mode:", font=("Segoe UI", 12)
        ).grid(row=4, column=2, sticky="w", padx=(14, 4), pady=8)
        self.hold_mode_frame = ctk.CTkFrame(self.settings_frame, fg_color="transparent")
        self.hold_mode_frame.grid(row=4, column=3, sticky="w", pady=8)
        self.hold_normal_button = ctk.CTkButton(
            self.hold_mode_frame, text="Normal", width=110,
            command=lambda: self._on_edit(lambda: self._set_hold_mode_selection("normal")),
        )
        self.hold_normal_button.grid(row=0, column=0, padx=(0, 6))
        self.hold_double_button = ctk.CTkButton(
            self.hold_mode_frame, text="Double-click", width=120,
            command=lambda: self._on_edit(lambda: self._set_hold_mode_selection("double-click")),
        )
        self.hold_double_button.grid(row=0, column=1)

        # Wait For Button sub-section
        self.wait_group = ctk.CTkFrame(
            self.settings_frame, corner_radius=12, fg_color="#1e1b2e",
            border_width=1, border_color=PURPLE_DARK,
        )
        self.wait_group.grid(
            row=5, column=0, columnspan=4, sticky="ew",
            padx=14, pady=(10, 12),
        )
        self.wait_group.grid_columnconfigure(0, weight=1)
        ctk.CTkLabel(
            self.wait_group,
            text="⏳ Wait For Button",
            font=("Segoe UI", 13, "bold"),
            text_color=PURPLE_LIGHT,
        ).grid(row=0, column=0, columnspan=3, sticky="w", padx=12, pady=(10, 6))
        self.wait_button_edit = ctk.CTkEntry(
            self.wait_group, placeholder_text="e.g. F, LButton, RButton",
            fg_color="#2a2640", border_color=PURPLE_DARK,
        )
        self.wait_button_edit.grid(row=1, column=0, sticky="ew", padx=12, pady=(0, 8))
        self.wait_button_edit.bind("<KeyRelease>", lambda e: self._on_edit())
        ctk.CTkButton(
            self.wait_group, text="Capture", width=90,
            fg_color=PURPLE_PRIMARY, hover_color=PURPLE_HOVER,
            command=self._capture_wait_button,
        ).grid(row=1, column=1, padx=(8, 6), pady=(0, 8))
        self.wait_enabled_checkbox = ctk.CTkCheckBox(
            self.wait_group, text="Enable wait",
            fg_color=PURPLE_PRIMARY, hover_color=PURPLE_HOVER,
            command=self._on_edit,
        )
        self.wait_enabled_checkbox.grid(row=1, column=2, padx=(0, 12), pady=(0, 8))

        # ── 4. ADVANCED SETTINGS ──────────────────────────────────────
        self.advanced_frame = ctk.CTkFrame(
            self.content_canvas, corner_radius=14, fg_color="#2a2640"
        )
        self.advanced_frame.grid(
            row=3, column=0, sticky="ew", padx=6, pady=(4, 8)
        )
        self.advanced_frame.grid_columnconfigure(0, weight=1)

        # Toggle button row
        adv_toggle_row = ctk.CTkFrame(self.advanced_frame, fg_color="transparent")
        adv_toggle_row.grid(row=0, column=0, sticky="ew", padx=8, pady=(6, 4))
        adv_toggle_row.grid_columnconfigure(1, weight=1)

        adv_icon = ctk.CTkLabel(
            adv_toggle_row, text="🔧", font=("Segoe UI", 16)
        )
        adv_icon.grid(row=0, column=0, sticky="w", padx=(6, 4))

        self.advanced_toggle_btn = ctk.CTkButton(
            adv_toggle_row,
            text="▶  Advanced Settings",
            font=("Segoe UI", 14, "bold"),
            fg_color=PURPLE_DARK,
            hover_color=PURPLE_PRIMARY,
            width=200,
            height=30,
            corner_radius=8,
            command=self._toggle_advanced,
        )
        self.advanced_toggle_btn.grid(row=0, column=1, sticky="w", padx=(4, 0))

        # Collapsible container for advanced content
        self.advanced_content = ctk.CTkFrame(
            self.advanced_frame, fg_color="transparent"
        )
        self.advanced_content.grid(row=1, column=0, sticky="ew", padx=4, pady=(0, 4))
        self.advanced_content.grid_columnconfigure(0, weight=1)

        # Auto-save interval row
        auto_save_row = ctk.CTkFrame(self.advanced_content, fg_color="#1e1b2e", corner_radius=10)
        auto_save_row.grid(row=0, column=0, sticky="ew", padx=8, pady=(4, 6))
        auto_save_row.grid_columnconfigure(1, weight=1)

        ctk.CTkLabel(
            auto_save_row,
            text="Auto-Save Interval (sec):",
            font=("Segoe UI", 12),
        ).grid(row=0, column=0, sticky="w", padx=12, pady=8)

        self.auto_save_spin = ctk.CTkEntry(
            auto_save_row, width=140,
            fg_color="#2a2640", border_color=PURPLE_DARK,
        )
        self.auto_save_spin.grid(row=0, column=1, sticky="w", pady=8)
        self.auto_save_spin.bind("<KeyRelease>", lambda e: self._on_edit())

        ctk.CTkLabel(
            auto_save_row,
            text="(10 – 3600 seconds)",
            font=("Segoe UI", 10),
            text_color="#9ca3af",
        ).grid(row=0, column=2, sticky="w", padx=(8, 12), pady=8)

        # Console log inside advanced
        self.console_log = ConsoleLog(self.advanced_content)
        self.console_log.grid(
            row=1, column=0, sticky="ew", padx=6, pady=(4, 8)
        )

        # Start hidden
        self._advanced_visible = False
        self.advanced_content.grid_remove()

        # ── 5. BOTTOM: Enable/Disable Toggle + Status + Alerts ────────
        bottom_frame = ctk.CTkFrame(
            self.main_frame, corner_radius=14, fg_color="#2a2640"
        )
        bottom_frame.grid(row=3, column=0, sticky="ew", pady=(4, 2))
        bottom_frame.grid_columnconfigure(0, weight=2)
        bottom_frame.grid_columnconfigure(1, weight=1)
        bottom_frame.grid_columnconfigure(2, weight=1)

        # Save button
        self.save_button = ctk.CTkButton(
            bottom_frame,
            text="💾 Save Settings",
            font=("Segoe UI", 13, "bold"),
            fg_color=PURPLE_PRIMARY,
            hover_color=PURPLE_HOVER,
            height=36,
            command=self._save_settings,
        )
        self.save_button.grid(row=0, column=0, sticky="w", padx=14, pady=12)

        # Central: status label (big, purple)
        self.status_var = tk.StringVar(value="○ Idle")
        self.status_label = ctk.CTkLabel(
            bottom_frame,
            textvariable=self.status_var,
            font=("Segoe UI", 18, "bold"),
            text_color=PURPLE_LIGHT,
        )
        self.status_label.grid(row=0, column=1, padx=8, pady=12)

        # Big enable/disable toggle button
        self.toggle_button = ctk.CTkButton(
            bottom_frame,
            text="▶  Enable",
            font=("Segoe UI", 15, "bold"),
            fg_color=GREEN_ENABLE,
            hover_color="#16a34a",
            height=44,
            width=160,
            command=self._toggle_runtime,
            corner_radius=10,
        )
        self.toggle_button.grid(row=0, column=2, sticky="e", padx=14, pady=12)

        # ── Hidden start/stop kept for reference but using toggle ──────
        self._update_mode_visibility()

    # ── Advanced Settings Toggle ──────────────────────────────────────

    def _toggle_advanced(self) -> None:
        """Show or hide the advanced settings panel."""
        self._advanced_visible = not self._advanced_visible
        if self._advanced_visible:
            self.advanced_content.grid()
            self.advanced_toggle_btn.configure(text="▼  Advanced Settings")
        else:
            self.advanced_content.grid_remove()
            self.advanced_toggle_btn.configure(text="▶  Advanced Settings")

    # ── Input Listeners ───────────────────────────────────────────────

    def _start_input_listeners(self) -> None:
        if pynput_mouse is None or pynput_keyboard is None:
            return
        self._mouse_listener = pynput_mouse.Listener(on_click=self._on_mouse_click)
        self._mouse_listener.start()
        self._keyboard_listener = pynput_keyboard.Listener(
            on_press=self._on_key_press, on_release=self._on_key_release
        )
        self._keyboard_listener.start()

    def _on_mouse_click(
        self, x: int, y: int, button: object, pressed: bool
    ) -> None:
        if button == pynput_mouse.Button.left:
            self._left_button_down = pressed
        elif button == pynput_mouse.Button.right:
            self._right_button_down = pressed
        self._push_input_state_to_dll()

    def _on_key_press(self, key: object) -> None:
        try:
            self._pressed_keys.add(key.char.lower())
        except AttributeError:
            self._pressed_keys.add(str(key).replace("'", ""))
        self._push_input_state_to_dll()

    def _on_key_release(self, key: object) -> None:
        try:
            self._pressed_keys.discard(key.char.lower())
        except AttributeError:
            self._pressed_keys.discard(str(key).replace("'", ""))
        self._push_input_state_to_dll()

    def _push_input_state_to_dll(self) -> None:
        """Push the latest mouse/keyboard state into the native DLL."""
        if not self._dll_loaded:
            return
        # Determine if the "wait" key is pressed
        wait_key_down = False
        if self.config.wait_enabled and self.config.wait_button:
            btn = self.config.wait_button.strip().lower()
            if btn in {"lbutton", "left", "mouse1"}:
                wait_key_down = self._left_button_down
            elif btn in {"rbutton", "right", "mouse2"}:
                wait_key_down = self._right_button_down
            else:
                wait_key_down = btn in self._pressed_keys
        self._dll_clicker.set_input_state(
            left_down=self._left_button_down,
            right_down=self._right_button_down,
            wait_key_down=wait_key_down,
        )

    # ── Profile & Data Management ─────────────────────────────────────

    def _ensure_default_profile_data(self) -> None:
        default_profile_path = profile_path(self.data_dir, "default")
        if not default_profile_path.exists():
            save_config(
                default_profile_path,
                sanitize_config(AppConfig(profile_name="default")),
            )
        runtime_state_path = self.data_dir / "runtime_state.json"
        if not runtime_state_path.exists():
            save_runtime_state(self.data_dir, enabled=False, profile_name="default")

    def _load_profiles(self) -> None:
        profiles = list_profiles(self.data_dir)
        self.profile_selector.configure(values=profiles)
        if self.current_profile in profiles:
            self.profile_selector.set(self.current_profile)
        else:
            self.profile_selector.set("default")
            self.current_profile = "default"

    def _on_profile_select(self, profile_name: str) -> None:
        """Handle profile switching – stop clicker, prompt unsaved, load."""
        # Auto-stop the clicker when switching profiles
        if self._clicking_enabled:
            self._stop_runtime_internal()
            self._set_status("⏸ Stopped – profile changed")
            self.console_log.write(f"Auto-stopped clicker due to profile switch", "WARNING")

        # If there are unsaved changes, offer to save them
        if self._unsaved_changes:
            answer = messagebox.askyesnocancel(
                "Unsaved Changes",
                f"You have unsaved changes in '{self.current_profile}'. "
                "Would you like to save them before switching?",
            )
            if answer is None:
                # Cancel – revert profile selector
                self.profile_selector.set(self.current_profile)
                return
            if answer:
                self._save_settings_internal()
        # Reset unsaved flag and load the new profile
        old_profile = self.current_profile
        self.current_profile = profile_name
        self.config = load_config(profile_path(self.data_dir, profile_name))
        self._unsaved_changes = False
        self._apply_config_to_form()
        self._update_unsaved_indicator()
        self._set_status(f"📂 Loaded profile: {profile_name}")
        self.console_log.write(f"Switched profile: '{old_profile}' → '{profile_name}'", "FILE")

    def _apply_config_to_form(self) -> None:
        self._set_mode_selection(self.config.mode)
        self._set_value(self.trigger_spin, self.config.trigger_cps)
        self._set_value(self.turbo_spin, self.config.turbo_cps)
        self._set_value(self.stop_spin, self.config.stop_delay)
        self._set_value(self.hold_spin, self.config.hold_delay)
        self._set_value(self.dbl_spin, self.config.dbl_interval)
        self._set_hold_mode_selection(self.config.hold_activation)
        self.wait_button_edit.delete(0, "end")
        self.wait_button_edit.insert(0, self.config.wait_button or "")
        if self.config.wait_enabled:
            self.wait_enabled_checkbox.select()
        else:
            self.wait_enabled_checkbox.deselect()
        self._set_value(self.auto_save_spin, self.config.auto_save_interval)
        self._update_mode_visibility()
        self._update_profile_info()

    def _set_value(self, entry: ctk.CTkEntry, value: int) -> None:
        entry.delete(0, "end")
        entry.insert(0, str(value))

    def _update_profile_info(self) -> None:
        """Show when the profile was last saved if possible."""
        prof_path = profile_path(self.data_dir, self.current_profile)
        if prof_path.exists():
            mtime = os.path.getmtime(prof_path)
            from datetime import datetime

            dt = datetime.fromtimestamp(mtime).strftime("%Y-%m-%d %H:%M")
            self.profile_info_var.set(f"Last saved: {dt}")
        else:
            self.profile_info_var.set("")

    def _update_unsaved_indicator(self) -> None:
        if self._unsaved_changes:
            self.unsaved_var.set("⚠ Unsaved")
            self.unsaved_label.configure(text_color="#fbbf24")
        else:
            self.unsaved_var.set("✓ Saved")
            self.unsaved_label.configure(text_color=PURPLE_LIGHT)

    # ── Mode Toggles ──────────────────────────────────────────────────

    def _set_mode_selection(self, mode: str) -> None:
        self._mode_value = mode
        is_hold = mode == "hold"
        self._set_toggle_style(self.mode_spam_button, not is_hold)
        self._set_toggle_style(self.mode_hold_button, is_hold)
        self._update_mode_visibility()

    def _set_hold_mode_selection(self, hold_mode: str) -> None:
        self._hold_mode_value = hold_mode
        is_double = hold_mode == "double-click"
        self._set_toggle_style(self.hold_normal_button, not is_double)
        self._set_toggle_style(self.hold_double_button, is_double)

    def _set_toggle_style(self, button: ctk.CTkButton, selected: bool) -> None:
        if selected:
            button.configure(
                fg_color=PURPLE_PRIMARY,
                hover_color=PURPLE_HOVER,
                text_color="white",
            )
        else:
            button.configure(
                fg_color=("#3b3b3b", "#2a2a2a"),
                hover_color=("#4b4b4b", "#3a3a3a"),
                text_color=("#cccccc", "#aaaaaa"),
            )

    # ── Config Collection ─────────────────────────────────────────────

    def _collect_config(self) -> AppConfig:
        config = AppConfig(
            profile_name=self.current_profile,
            mode=self._mode_value,
            trigger_cps=int(self.trigger_spin.get() or 0),
            turbo_cps=int(self.turbo_spin.get() or 0),
            stop_delay=int(self.stop_spin.get() or 0),
            hold_delay=int(self.hold_spin.get() or 0),
            dbl_interval=int(self.dbl_spin.get() or 0),
            hold_activation=self._hold_mode_value,
            wait_button=self.wait_button_edit.get().strip(),
            wait_enabled=self.wait_enabled_checkbox.get() == 1,
            universal_enabled=False,  # replaced by toggle
            auto_save_interval=int(self.auto_save_spin.get() or 0),
        )
        return sanitize_config(config)

    # ── Profile CRUD ──────────────────────────────────────────────────

    def _create_profile(self) -> None:
        name = simpledialog.askstring("New Profile", "Enter a profile name:")
        if not name or not name.strip():
            return
        profile = name.strip().replace(" ", "_")
        target = profile_path(self.data_dir, profile)
        if target.exists():
            messagebox.showwarning("Profile Exists", "That profile already exists.")
            return
        save_config(target, sanitize_config(AppConfig(profile_name=profile)))
        self.current_profile = profile
        self._unsaved_changes = False
        self._load_profiles()
        self._update_unsaved_indicator()
        self._set_status(f"📁 Created profile: {profile}")
        self.console_log.write(f"Created profile file: {target}", "FILE")

    def _delete_profile(self) -> None:
        if not self.current_profile or self.current_profile == "default":
            messagebox.showinfo("Protected", "The default profile cannot be deleted.")
            return
        # Stop if running
        if self._clicking_enabled:
            self._stop_runtime_internal()
        # Confirm
        if not messagebox.askyesno(
            "Delete Profile",
            f"Are you sure you want to delete '{self.current_profile}'?",
        ):
            return
        path = profile_path(self.data_dir, self.current_profile)
        deleted_name = self.current_profile
        if path.exists():
            path.unlink()
            self.console_log.write(f"Deleted profile file: {path}", "FILE")
        self.current_profile = "default"
        self._unsaved_changes = False
        self._load_profiles()
        self.config = load_config(profile_path(self.data_dir, self.current_profile))
        self._apply_config_to_form()
        self._update_unsaved_indicator()
        self._set_status("🗑 Profile deleted")
        self.console_log.write(f"Profile '{deleted_name}' deleted, switched to 'default'", "FILE")

    # ── Saving ────────────────────────────────────────────────────────

    def _save_settings(self) -> None:
        """Public save – called by Save button."""
        self._save_settings_internal()

    def _save_settings_internal(self) -> bool:
        """Internal save. Returns True on success."""
        self.config = self._collect_config()
        errors = validate_settings(self.config)
        if errors:
            self.console_log.write(f"Validation errors: {', '.join(errors)}", "WARNING")
            messagebox.showwarning(
                "Invalid Settings",
                f"These values need attention: {', '.join(errors)}",
            )
            return False
        save_path = profile_path(self.data_dir, self.current_profile)
        save_config(save_path, self.config)
        self._write_active_profile(self.current_profile)
        self._unsaved_changes = False
        self._update_unsaved_indicator()
        self._update_profile_info()
        self._set_status("💾 Settings saved successfully")
        self.console_log.write(f"Settings saved to: {save_path}", "FILE")
        return True

    def _capture_wait_button(self) -> None:
        key_name = simpledialog.askstring(
            "Capture button",
            "Enter a button name such as F, LButton, or RButton:",
        )
        if key_name and key_name.strip():
            self.wait_button_edit.delete(0, "end")
            self.wait_button_edit.insert(0, key_name.strip())
            self._set_status("🎯 Wait button updated")
            self._on_edit()

    def _write_active_profile(self, profile_name: str) -> None:
        active_profile_path = self.data_dir / "active_profile.txt"
        active_profile_path.write_text(profile_name, encoding="utf-8")

    # ── Edit Detection & Auto-Disable ─────────────────────────────────

    def _on_edit(self, callback=None) -> None:
        """Called when the user starts editing any setting."""
        # Auto-stop the clicker when user changes settings
        if self._clicking_enabled:
            self._stop_runtime_internal()
            self._set_status("⏸ Auto-stopped – settings changed")
            self.console_log.write("Auto-stopped clicker due to settings change", "WARNING")

        # Mark unsaved
        if not self._unsaved_changes:
            self._unsaved_changes = True
            self._update_unsaved_indicator()

        # Execute optional callback (for mode buttons etc.)
        if callback:
            callback()

    # ── DLL Update & Runtime Toggle ──────────────────────────────────

    def _sync_config_to_dll(self) -> None:
        """Push the current GUI config into the native DLL."""
        if not self._dll_loaded:
            return
        self._dll_clicker.update_config_from_app(
            trigger_cps=self.config.trigger_cps,
            turbo_cps=self.config.turbo_cps,
            stop_delay=self.config.stop_delay,
            hold_delay=self.config.hold_delay,
            dbl_interval=self.config.dbl_interval,
            mode=self.config.mode,
            hold_activation=self.config.hold_activation,
            wait_enabled=self.config.wait_enabled,
            wait_button=self.config.wait_button,
        )

    def _toggle_runtime(self) -> None:
        if self._clicking_enabled:
            self._stop_runtime()
        else:
            self._start_runtime()

    def _start_runtime(self) -> None:
        # Check for unsaved changes and offer to save
        if self._unsaved_changes:
            answer = messagebox.askyesnocancel(
                "Unsaved Changes",
                "You have unsaved settings. Would you like to save them first?",
            )
            if answer is None:
                return  # Cancel
            if answer:
                if not self._save_settings_internal():
                    return  # Save failed, don't start

        self.config = self._collect_config()
        errors = validate_settings(self.config)
        if errors:
            self.console_log.write(f"Cannot start: validation errors: {', '.join(errors)}", "ERROR")
            messagebox.showwarning(
                "Invalid Settings",
                f"These values need attention: {', '.join(errors)}",
            )
            return

        # Save current state
        save_path = profile_path(self.data_dir, self.current_profile)
        save_config(save_path, self.config)
        self._write_active_profile(self.current_profile)
        save_runtime_state(
            self.data_dir, enabled=True, profile_name=self.current_profile
        )
        self.runtime_state = load_runtime_state(self.data_dir)

        # ── Start the native DLL clicker ──────────────────────────────
        if self._dll_loaded:
            # Init DLL with current config
            self._dll_clicker.init_from_app_config(
                trigger_cps=self.config.trigger_cps,
                turbo_cps=self.config.turbo_cps,
                stop_delay=self.config.stop_delay,
                hold_delay=self.config.hold_delay,
                dbl_interval=self.config.dbl_interval,
                mode=self.config.mode,
                hold_activation=self.config.hold_activation,
                wait_enabled=self.config.wait_enabled,
                wait_button=self.config.wait_button,
            )
            # Push initial input state
            self._push_input_state_to_dll()
            # Start the native thread
            self._dll_clicker.start()
        else:
            # Fallback: start Python-based clicker
            self._start_python_clicker()

        self._clicking_enabled = True
        self._unsaved_changes = False
        self._update_unsaved_indicator()
        self._update_toggle_ui(enabled=True)
        engine = "DLL" if self._dll_loaded else "Python (fallback)"
        mode_str = self.config.mode
        cps_str = self.config.trigger_cps if mode_str == "spam" else f"hold_delay={self.config.hold_delay}ms"
        self._set_status("▶ Running – clicker active")
        self.console_log.write(
            f"Clicker STARTED [engine={engine}] | profile='{self.current_profile}' | mode={mode_str} | {cps_str}",
            "SUCCESS",
        )

    def _stop_runtime(self) -> None:
        self._stop_runtime_internal()
        save_runtime_state(
            self.data_dir, enabled=False, profile_name=self.current_profile
        )
        self.runtime_state = load_runtime_state(self.data_dir)
        self._update_toggle_ui(enabled=False)
        self._set_status("■ Stopped – clicker disabled")
        self.console_log.write("Clicker STOPPED", "CLICKER")

    def _stop_runtime_internal(self) -> None:
        """Stop clicker without updating UI state (used internally)."""
        if self._dll_loaded and self._clicking_enabled:
            self._dll_clicker.stop()
        else:
            self._stop_python_clicker()
        self._clicking_enabled = False
        self._update_toggle_ui(enabled=False)

    def _update_toggle_ui(self, enabled: bool) -> None:
        if enabled:
            self.toggle_button.configure(
                text="⏹  Disable",
                fg_color=RED_DISABLE,
                hover_color="#b91c1c",
            )
        else:
            self.toggle_button.configure(
                text="▶  Enable",
                fg_color=GREEN_ENABLE,
                hover_color="#16a34a",
            )

    def _set_status(self, message: str) -> None:
        self.status_var.set(message)

    # ── Clicker Engine ────────────────────────────────────────────────

    def _start_python_clicker(self) -> None:
        if self._click_thread and self._click_thread.is_alive():
            self.console_log.write("Clicker thread already running, ignoring start", "WARNING")
            return
        self._clicking_event.set()
        self._click_thread = threading.Thread(
            target=self._python_click_loop, daemon=True
        )
        self._click_thread.start()

    def _stop_python_clicker(self) -> None:
        self._clicking_event.clear()
        # Do not join — thread is daemon, will exit on next loop check

    def _wait_requirements_met(self) -> bool:
        if not self.config.wait_enabled or not self.config.wait_button:
            return True
        button_name = self.config.wait_button.strip().lower()
        if button_name in {"lbutton", "left", "mouse1"}:
            return self._left_button_down
        if button_name in {"rbutton", "right", "mouse2"}:
            return self._right_button_down
        return button_name in self._pressed_keys

    def _python_click_loop(self) -> None:
        """Main autoclicker loop – fixed logic with safety checks."""
        last_activity_time = time.monotonic()
        click_count = 0

        while self._clicking_event.is_set():
            # ── Safety: idle timeout check ─────────────────────────────
            # If no LMB activity for `_idle_timeout` seconds, auto-stop
            if self._left_button_down:
                last_activity_time = time.monotonic()
            elif (time.monotonic() - last_activity_time) > self._idle_timeout:
                self.console_log.write(
                    f"SAFETY: No input for {self._idle_timeout}s — auto-stopping clicker",
                    "WARNING",
                )
                # Schedule stop on main thread
                self.after(0, self._stop_runtime)
                break

            # ── Safety: re-check clicking_event after every operation ──
            if self._mode_value == "hold":
                if self.config.hold_activation == "double-click":
                    self._handle_double_click_mode()
                else:
                    self._handle_normal_hold_mode()
                # Small sleep to prevent CPU spin
                time.sleep(0.002)
                continue

            # ── Spam mode ──────────────────────────────────────────────
            # Only click when left button is held and wait requirements met
            if self._left_button_down and self._wait_requirements_met():
                self._emit_click()
                click_count += 1
                # Use trigger_cps as the base spam rate
                delay = 1.0 / max(1, self.config.trigger_cps)
                # Break the delay into micro-sleeps for responsive stopping
                self._sleeper_with_checks(delay)
            else:
                # No button held — small sleep to avoid busy-wait
                time.sleep(0.005)

        # Loop exited
        self.console_log.write(
            f"Clicker loop ended (total clicks this session: ~{click_count})",
            "CLICKER",
        )

    def _sleeper_with_checks(self, total_seconds: float) -> None:
        """Sleep for `total_seconds` but check `_clicking_event` and button
        state multiple times to enable responsive stopping and safety."""
        chunk = 0.01  # 10ms chunks for responsive checks
        elapsed = 0.0
        while elapsed < total_seconds:
            if not self._clicking_event.is_set():
                return  # Stop signal received
            # In spam mode, also check if we should stop clicking (button released)
            if self._mode_value == "spam" and not self._left_button_down:
                return
            remaining = total_seconds - elapsed
            sleep_time = min(chunk, remaining)
            time.sleep(sleep_time)
            elapsed += sleep_time

    def _handle_double_click_mode(self) -> None:
        """Double-click hold mode – detect edge-triggered press and fire two clicks."""
        # Detect rising edge: button was NOT down, but IS now down
        if self._left_button_down and not self._left_was_down:
            # Fresh press detected — fire double-click
            self.console_log.write("Double-click triggered on press", "CLICKER")
            self._emit_click()
            self._double_click_armed = False
            # Sleep for dbl_interval between clicks, with safety checks
            delay = self.config.dbl_interval / 1000.0
            self._sleeper_with_checks(delay)
            if self._clicking_event.is_set() and self._left_button_down:
                self._emit_click()
                self.console_log.write("Second click fired (double-click)", "CLICKER")
        # Update edge tracking
        self._left_was_down = self._left_button_down

    def _handle_normal_hold_mode(self) -> None:
        """Normal hold mode – repeatedly click at hold_delay interval
        with accurate monotonic timing and mid-cycle safety checks."""
        if self._left_button_down and self._wait_requirements_met():
            if self._hold_since is None:
                self._hold_since = time.monotonic()
                return  # wait until first interval elapses

            elapsed_ms = (time.monotonic() - self._hold_since) * 1000
            if elapsed_ms >= self.config.hold_delay:
                # Multiple safety checks before firing
                if not self._clicking_event.is_set():
                    self._hold_since = None
                    return
                if not self._left_button_down:
                    self._hold_since = None
                    return

                self._emit_click()
                # Reset timer relative to last fire time (not absolute)
                # This prevents drift: offset timer by exactly hold_delay
                self._hold_since += self.config.hold_delay / 1000.0

                # Prevent timer falling behind if hold_delay was very short
                now = time.monotonic()
                if self._hold_since < now - 0.5:
                    self._hold_since = now
        else:
            self._hold_since = None

    def _emit_click(self) -> None:
        """Perform a mouse click using pynput (Python fallback when DLL is unavailable)."""
        if pynput_mouse is None:
            self.console_log.write("pynput mouse not available, cannot click", "ERROR")
            return
        try:
            controller = pynput_mouse.Controller()
            controller.click(pynput_mouse.Button.left)
        except Exception as exc:
            self.console_log.write(f"Click error: {exc}", "ERROR")

    # ── Visibility Helpers ────────────────────────────────────────────

    def _update_mode_visibility(self) -> None:
        is_hold = self._mode_value == "hold"
        self._set_visibility(self.trigger_spin, not is_hold)
        self._set_visibility(self.turbo_spin, not is_hold)
        self._set_visibility(self.stop_spin, not is_hold)
        self._set_visibility(self.hold_spin, is_hold)
        self._set_visibility(self.dbl_spin, is_hold)
        self._set_visibility(self.hold_mode_frame, is_hold)
        self._set_visibility(self.wait_group, is_hold)

    def _set_visibility(self, widget: tk.Widget, visible: bool) -> None:
        if visible:
            widget.grid()
        else:
            widget.grid_remove()

    # ── Runtime State Restoration ─────────────────────────────────────

    def _apply_runtime_state(self) -> None:
        is_enabled = bool(self.runtime_state.get("enabled", False))
        self._update_toggle_ui(enabled=is_enabled)
        profile_name = self.runtime_state.get("profile_name", "default")
        if profile_name in list_profiles(self.data_dir):
            self.current_profile = profile_name
            self.profile_selector.set(profile_name)
            self.config = load_config(
                profile_path(self.data_dir, profile_name)
            )
            self._apply_config_to_form()


# ── Entry point ────────────────────────────────────────────────────────


def main() -> int:
    if _is_headless_environment():
        print(
            "No display available; start the GUI on a desktop session "
            "or set DISPLAY."
        )
        return 1

    try:
        window = AutoClickerWindow()
    except tk.TclError as exc:
        print(f"Unable to start the GUI: {exc}")
        print(
            "Make sure Tk/Tcl is installed and you are running "
            "in a desktop session."
        )
        return 1

    try:
        window.mainloop()
    except KeyboardInterrupt:
        return 0

    return 0


if __name__ == "__main__":
    sys.exit(main())

