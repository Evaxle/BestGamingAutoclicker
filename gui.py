from __future__ import annotations

import os
import sys
import threading
import time
import tkinter as tk
from pathlib import Path
from tkinter import messagebox, simpledialog

import customtkinter as ctk

try:
    from pynput import keyboard as pynput_keyboard, mouse as pynput_mouse
except Exception:  # pragma: no cover - headless fallback
    pynput_keyboard = None
    pynput_mouse = None

try:
    import pyautogui
except Exception:  # pragma: no cover - headless fallback
    pyautogui = None

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


class AutoClickerWindow(ctk.CTk):
    def __init__(self) -> None:
        super().__init__()
        ctk.set_appearance_mode("dark")
        ctk.set_default_color_theme("dark-purple")

        self.title("Autoclicker")
        self.geometry("820x700")
        self.minsize(760, 640)

        self.data_dir = Path(__file__).resolve().parent / "data"
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.current_profile = "default"
        self.config = sanitize_config(AppConfig(profile_name=self.current_profile))
        self._ensure_default_profile_data()
        self.runtime_state = load_runtime_state(self.data_dir)
        self._mode_value = "spam"
        self._hold_mode_value = "normal"
        self._clicking = False
        self._left_button_down = False
        self._right_button_down = False
        self._pressed_keys: set[str] = set()
        self._hold_since: float | None = None
        self._double_click_ready = False
        self._last_click_time = 0.0
        self._click_thread: threading.Thread | None = None
        self._mouse_listener = None
        self._keyboard_listener = None

        self._build_ui()
        self._load_profiles()
        self._apply_config_to_form()
        self._apply_runtime_state()
        self._start_input_listeners()

    def _build_ui(self) -> None:
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(0, weight=1)

        self.main_frame = ctk.CTkFrame(self, corner_radius=16)
        self.main_frame.grid(row=0, column=0, sticky="nsew", padx=20, pady=20)
        self.main_frame.grid_columnconfigure(0, weight=1)

        title = ctk.CTkLabel(self.main_frame, text="Autoclicker", font=("Segoe UI", 24, "bold"))
        title.grid(row=0, column=0, sticky="w", pady=(10, 4))

        subtitle = ctk.CTkLabel(self.main_frame, text="Python-powered autoclicking with profile-based settings", text_color="gray70")
        subtitle.grid(row=1, column=0, sticky="w", pady=(0, 12))

        self.status_var = tk.StringVar(value="Idle")
        self.status_label = ctk.CTkLabel(self.main_frame, textvariable=self.status_var, font=("Segoe UI", 12, "bold"), text_color="#fbbf24")
        self.status_label.grid(row=2, column=0, sticky="e", pady=(0, 10))

        self.profile_frame = ctk.CTkFrame(self.main_frame, corner_radius=16)
        self.profile_frame.grid(row=3, column=0, sticky="ew", padx=4, pady=6)
        self.profile_frame.grid_columnconfigure(0, weight=1)
        ctk.CTkLabel(self.profile_frame, text="Profiles", font=("Segoe UI", 15, "bold")).grid(row=0, column=0, sticky="w", padx=12, pady=(8, 6))
        self.profile_selector = ctk.CTkComboBox(self.profile_frame, values=["default"], state="readonly")
        self.profile_selector.grid(row=1, column=0, sticky="ew", padx=12, pady=(0, 6))
        self.profile_selector.bind("<<ComboboxSelected>>", lambda _event: self._select_profile(self.profile_selector.get()))

        profile_actions = ctk.CTkFrame(self.profile_frame, fg_color="transparent")
        profile_actions.grid(row=2, column=0, sticky="ew", padx=12, pady=(0, 10))
        ctk.CTkButton(profile_actions, text="New", command=self._create_profile).grid(row=0, column=0, padx=(0, 8))
        ctk.CTkButton(profile_actions, text="Delete", command=self._delete_profile).grid(row=0, column=1)

        self.settings_frame = ctk.CTkFrame(self.main_frame, corner_radius=16)
        self.settings_frame.grid(row=4, column=0, sticky="ew", padx=4, pady=6)
        self.settings_frame.grid_columnconfigure(1, weight=1)
        self.settings_frame.grid_columnconfigure(3, weight=1)
        ctk.CTkLabel(self.settings_frame, text="Mode & Settings", font=("Segoe UI", 15, "bold")).grid(row=0, column=0, columnspan=4, sticky="w", padx=12, pady=(8, 10))

        ctk.CTkLabel(self.settings_frame, text="Mode:").grid(row=1, column=0, sticky="w", padx=12, pady=6)
        self.mode_frame = ctk.CTkFrame(self.settings_frame, fg_color="transparent")
        self.mode_frame.grid(row=1, column=1, sticky="w", pady=6)
        self.mode_spam_button = ctk.CTkButton(self.mode_frame, text="Spam", width=90, command=lambda: self._set_mode_selection("spam"))
        self.mode_spam_button.grid(row=0, column=0, padx=(0, 6))
        self.mode_hold_button = ctk.CTkButton(self.mode_frame, text="Hold", width=90, command=lambda: self._set_mode_selection("hold"))
        self.mode_hold_button.grid(row=0, column=1)

        ctk.CTkLabel(self.settings_frame, text="Trigger CPS:").grid(row=2, column=0, sticky="w", padx=12, pady=6)
        self.trigger_spin = ctk.CTkEntry(self.settings_frame, width=140)
        self.trigger_spin.grid(row=2, column=1, sticky="w", pady=6)

        ctk.CTkLabel(self.settings_frame, text="Turbo CPS:").grid(row=2, column=2, sticky="w", padx=12, pady=6)
        self.turbo_spin = ctk.CTkEntry(self.settings_frame, width=140)
        self.turbo_spin.grid(row=2, column=3, sticky="w", pady=6)

        ctk.CTkLabel(self.settings_frame, text="Stop Delay:").grid(row=3, column=0, sticky="w", padx=12, pady=6)
        self.stop_spin = ctk.CTkEntry(self.settings_frame, width=140)
        self.stop_spin.grid(row=3, column=1, sticky="w", pady=6)

        ctk.CTkLabel(self.settings_frame, text="Hold Delay:").grid(row=3, column=2, sticky="w", padx=12, pady=6)
        self.hold_spin = ctk.CTkEntry(self.settings_frame, width=140)
        self.hold_spin.grid(row=3, column=3, sticky="w", pady=6)

        ctk.CTkLabel(self.settings_frame, text="Double Interval:").grid(row=4, column=0, sticky="w", padx=12, pady=6)
        self.dbl_spin = ctk.CTkEntry(self.settings_frame, width=140)
        self.dbl_spin.grid(row=4, column=1, sticky="w", pady=6)

        ctk.CTkLabel(self.settings_frame, text="Hold Mode:").grid(row=4, column=2, sticky="w", padx=12, pady=6)
        self.hold_mode_frame = ctk.CTkFrame(self.settings_frame, fg_color="transparent")
        self.hold_mode_frame.grid(row=4, column=3, sticky="w", pady=6)
        self.hold_normal_button = ctk.CTkButton(self.hold_mode_frame, text="Normal", width=100, command=lambda: self._set_hold_mode_selection("normal"))
        self.hold_normal_button.grid(row=0, column=0, padx=(0, 6))
        self.hold_double_button = ctk.CTkButton(self.hold_mode_frame, text="Double-click", width=110, command=lambda: self._set_hold_mode_selection("double-click"))
        self.hold_double_button.grid(row=0, column=1)

        self.wait_group = ctk.CTkFrame(self.settings_frame, corner_radius=12)
        self.wait_group.grid(row=5, column=0, columnspan=4, sticky="ew", padx=12, pady=(8, 10))
        self.wait_group.grid_columnconfigure(1, weight=1)
        ctk.CTkLabel(self.wait_group, text="Wait For Button", font=("Segoe UI", 13, "bold")).grid(row=0, column=0, columnspan=3, sticky="w", padx=10, pady=(10, 6))
        self.wait_button_edit = ctk.CTkEntry(self.wait_group, placeholder_text="e.g. F, LButton, RButton")
        self.wait_button_edit.grid(row=1, column=0, sticky="ew", padx=10, pady=(0, 6))
        ctk.CTkButton(self.wait_group, text="Capture", width=80, command=self._capture_wait_button).grid(row=1, column=1, padx=(8, 10), pady=(0, 6))
        self.wait_enabled_checkbox = ctk.CTkCheckBox(self.wait_group, text="Enable wait")
        self.wait_enabled_checkbox.grid(row=1, column=2, padx=(0, 10), pady=(0, 6))

        self.enable_checkbox = ctk.CTkCheckBox(self.settings_frame, text="Enable AutoClicker")
        self.enable_checkbox.grid(row=6, column=0, columnspan=4, sticky="w", padx=12, pady=(0, 10))

        buttons_frame = ctk.CTkFrame(self.main_frame, fg_color="transparent")
        buttons_frame.grid(row=5, column=0, sticky="ew", padx=4, pady=(4, 8))
        ctk.CTkButton(buttons_frame, text="Save Settings", command=self._save_settings).grid(row=0, column=0, padx=(0, 8))
        self.start_button = ctk.CTkButton(buttons_frame, text="Start", command=self._start_runtime)
        self.start_button.grid(row=0, column=1, padx=(0, 8))
        self.stop_button = ctk.CTkButton(buttons_frame, text="Stop", command=self._stop_runtime)
        self.stop_button.grid(row=0, column=2)

        self._update_mode_visibility()

    def _start_input_listeners(self) -> None:
        if pynput_mouse is None or pynput_keyboard is None:
            return
        self._mouse_listener = pynput_mouse.Listener(on_click=self._on_mouse_click)
        self._mouse_listener.start()
        self._keyboard_listener = pynput_keyboard.Listener(on_press=self._on_key_press, on_release=self._on_key_release)
        self._keyboard_listener.start()

    def _on_mouse_click(self, x: int, y: int, button: object, pressed: bool) -> None:
        if button == pynput_mouse.Button.left:
            self._left_button_down = pressed
            if not pressed:
                self._hold_since = None
                self._double_click_ready = False
        elif button == pynput_mouse.Button.right:
            self._right_button_down = pressed

    def _on_key_press(self, key: object) -> None:
        try:
            self._pressed_keys.add(key.char.lower())
        except AttributeError:
            self._pressed_keys.add(str(key).replace("'", ""))

    def _on_key_release(self, key: object) -> None:
        try:
            self._pressed_keys.discard(key.char.lower())
        except AttributeError:
            self._pressed_keys.discard(str(key).replace("'", ""))

    def _ensure_default_profile_data(self) -> None:
        default_profile_path = profile_path(self.data_dir, "default")
        if not default_profile_path.exists():
            save_config(default_profile_path, sanitize_config(AppConfig(profile_name="default")))

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

    def _select_profile(self, profile_name: str) -> None:
        self.current_profile = profile_name
        self.config = load_config(profile_path(self.data_dir, profile_name))
        self._apply_config_to_form()

    def _apply_config_to_form(self) -> None:
        self._set_mode_selection(self.config.mode)
        self.trigger_spin.delete(0, "end")
        self.trigger_spin.insert(0, str(self.config.trigger_cps))
        self.turbo_spin.delete(0, "end")
        self.turbo_spin.insert(0, str(self.config.turbo_cps))
        self.stop_spin.delete(0, "end")
        self.stop_spin.insert(0, str(self.config.stop_delay))
        self.hold_spin.delete(0, "end")
        self.hold_spin.insert(0, str(self.config.hold_delay))
        self.dbl_spin.delete(0, "end")
        self.dbl_spin.insert(0, str(self.config.dbl_interval))
        self._set_hold_mode_selection(self.config.hold_activation)
        self.wait_button_edit.delete(0, "end")
        self.wait_button_edit.insert(0, self.config.wait_button or "")
        self.wait_enabled_checkbox.select() if self.config.wait_enabled else self.wait_enabled_checkbox.deselect()
        self.enable_checkbox.select() if self.config.universal_enabled else self.enable_checkbox.deselect()
        self._update_mode_visibility()

    def _set_mode_selection(self, mode: str) -> None:
        self._mode_value = mode
        is_hold = mode == "hold"
        self._set_toggle_button_state(self.mode_spam_button, not is_hold)
        self._set_toggle_button_state(self.mode_hold_button, is_hold)
        self._update_mode_visibility()

    def _set_hold_mode_selection(self, hold_mode: str) -> None:
        self._hold_mode_value = hold_mode
        is_double = hold_mode == "double-click"
        self._set_toggle_button_state(self.hold_normal_button, not is_double)
        self._set_toggle_button_state(self.hold_double_button, is_double)

    def _set_toggle_button_state(self, button: ctk.CTkButton, selected: bool) -> None:
        if selected:
            button.configure(fg_color="#7c3aed", hover_color="#6d28d9")
        else:
            button.configure(fg_color=("#2b2b2b", "#1f1f1f"), hover_color=("#3b3b3b", "#2a2a2a"))

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
            universal_enabled=self.enable_checkbox.get() == 1,
        )
        return sanitize_config(config)

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
        self._load_profiles()
        self._set_status("Created profile")

    def _delete_profile(self) -> None:
        if not self.current_profile or self.current_profile == "default":
            messagebox.showinfo("Protected", "The default profile cannot be deleted.")
            return
        path = profile_path(self.data_dir, self.current_profile)
        if path.exists():
            path.unlink()
        self.current_profile = "default"
        self._load_profiles()
        self.config = load_config(profile_path(self.data_dir, self.current_profile))
        self._apply_config_to_form()

    def _save_settings(self) -> None:
        self.config = self._collect_config()
        errors = validate_settings(self.config)
        if errors:
            messagebox.showwarning("Invalid Settings", f"These values need attention: {', '.join(errors)}")
            return
        save_config(profile_path(self.data_dir, self.current_profile), self.config)
        self._write_active_profile(self.current_profile)
        self._set_status("Settings saved")

    def _capture_wait_button(self) -> None:
        key_name = simpledialog.askstring("Capture button", "Enter a button name such as F, LButton, or RButton:")
        if key_name and key_name.strip():
            self.wait_button_edit.delete(0, "end")
            self.wait_button_edit.insert(0, key_name.strip())
            self._set_status("Wait button updated")

    def _set_status(self, message: str) -> None:
        self.status_var.set(message)

    def _update_mode_visibility(self) -> None:
        is_hold = self._mode_value == "hold"
        self._set_widget_visibility(self.trigger_spin, not is_hold)
        self._set_widget_visibility(self.turbo_spin, not is_hold)
        self._set_widget_visibility(self.stop_spin, not is_hold)
        self._set_widget_visibility(self.hold_spin, is_hold)
        self._set_widget_visibility(self.dbl_spin, is_hold)
        self._set_widget_visibility(self.hold_mode_frame, is_hold)
        self._set_widget_visibility(self.wait_group, is_hold)

    def _set_widget_visibility(self, widget: tk.Widget, visible: bool) -> None:
        if visible:
            widget.grid()
        else:
            widget.grid_remove()

    def _apply_runtime_state(self) -> None:
        self.start_button.configure(state="normal" if not self.runtime_state.get("enabled", False) else "disabled")
        self.stop_button.configure(state="normal" if bool(self.runtime_state.get("enabled", False)) else "disabled")
        profile_name = self.runtime_state.get("profile_name", "default")
        if profile_name in list_profiles(self.data_dir):
            self.current_profile = profile_name
            self.profile_selector.set(profile_name)
            self.config = load_config(profile_path(self.data_dir, profile_name))
            self._apply_config_to_form()

    def _write_active_profile(self, profile_name: str) -> None:
        active_profile_path = self.data_dir / "active_profile.txt"
        active_profile_path.write_text(profile_name, encoding="utf-8")

    def _start_runtime(self) -> None:
        self.config = self._collect_config()
        errors = validate_settings(self.config)
        if errors:
            messagebox.showwarning("Invalid Settings", f"These values need attention: {', '.join(errors)}")
            return
        save_config(profile_path(self.data_dir, self.current_profile), self.config)
        self._write_active_profile(self.current_profile)
        save_runtime_state(self.data_dir, enabled=True, profile_name=self.current_profile)
        self.runtime_state = load_runtime_state(self.data_dir)
        self._apply_runtime_state()
        self._start_python_clicker()
        self._set_status("Runtime started")

    def _stop_runtime(self) -> None:
        save_runtime_state(self.data_dir, enabled=False, profile_name=self.current_profile)
        self.runtime_state = load_runtime_state(self.data_dir)
        self._apply_runtime_state()
        self._stop_python_clicker()
        self._set_status("Runtime stopped")

    def _start_python_clicker(self) -> None:
        if self._click_thread and self._click_thread.is_alive():
            return
        self._clicking = True
        self._click_thread = threading.Thread(target=self._python_click_loop, daemon=True)
        self._click_thread.start()

    def _stop_python_clicker(self) -> None:
        self._clicking = False

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
        while self._clicking:
            time.sleep(0.01)
            if not self._clicking:
                break
            if self._mode_value == "hold":
                if self.config.hold_activation == "double-click":
                    if self._left_button_down and self._double_click_ready:
                        self._double_click_ready = False
                        self._emit_click()
                    elif self._left_button_down:
                        now = time.time()
                        if self._last_click_time and (now - self._last_click_time) <= (self.config.dbl_interval / 1000):
                            self._double_click_ready = True
                        self._last_click_time = now
                    continue

                if self._left_button_down and self._wait_requirements_met():
                    if self._hold_since is None:
                        self._hold_since = time.monotonic()
                    if self._hold_since is not None and (time.monotonic() - self._hold_since) * 1000 >= self.config.hold_delay:
                        self._emit_click()
                        self._hold_since = time.monotonic()
                else:
                    self._hold_since = None
                continue

            if self._left_button_down and self._wait_requirements_met():
                self._emit_click()
                time.sleep(max(0.001, 1.0 / max(1, self.config.trigger_cps)))

    def _emit_click(self) -> None:
        if pyautogui is None:
            return
        try:
            pyautogui.click()
        except Exception:
            pass


def main() -> int:
    if os.name != "nt" and not os.environ.get("DISPLAY"):
        print("No display available; start the GUI on a desktop session or set DISPLAY.")
        return 1

    try:
        window = AutoClickerWindow()
    except tk.TclError as exc:
        print(f"Unable to start the GUI: {exc}")
        print("Make sure Tk/Tcl is installed and you are running in a desktop session.")
        return 1

    try:
        window.mainloop()
    except KeyboardInterrupt:
        return 0

    return 0


if __name__ == "__main__":
    sys.exit(main())
