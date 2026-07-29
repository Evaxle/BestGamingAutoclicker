from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import configparser
from typing import List, Dict


@dataclass
class AppConfig:
    profile_name: str = "default"
    mode: str = "spam"
    trigger_cps: int = 5
    turbo_cps: int = 70
    stop_delay: int = 1000
    hold_delay: int = 200
    dbl_interval: int = 300
    hold_activation: str = "normal"
    wait_button: str = ""
    wait_enabled: bool = False
    universal_enabled: bool = False


DEFAULT_CONFIG = AppConfig()


def _coerce_int(value: object, fallback: int, minimum: int = 1) -> int:
    try:
        parsed = int(value)
    except (TypeError, ValueError):
        return fallback
    return max(minimum, parsed)


def _coerce_bool(value: object) -> bool:
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return bool(value)
    if isinstance(value, str):
        lowered = value.strip().lower()
        if lowered in {"1", "true", "yes", "on"}:
            return True
        if lowered in {"0", "false", "no", "off", ""}:
            return False
    return False


def validate_settings(config: AppConfig) -> List[str]:
    errors: List[str] = []
    if config.mode not in {"spam", "hold"}:
        errors.append("mode")
    if config.trigger_cps < 1 or config.trigger_cps > 200:
        errors.append("trigger_cps")
    if config.turbo_cps < 1 or config.turbo_cps > 500:
        errors.append("turbo_cps")
    if config.stop_delay < 0 or config.stop_delay > 10000:
        errors.append("stop_delay")
    if config.hold_delay < 0 or config.hold_delay > 10000:
        errors.append("hold_delay")
    if config.dbl_interval < 0 or config.dbl_interval > 5000:
        errors.append("dbl_interval")
    if config.hold_activation not in {"normal", "double-click"}:
        errors.append("hold_activation")
    return errors


def sanitize_config(config: AppConfig) -> AppConfig:
    safe = AppConfig(
        profile_name=config.profile_name or "default",
        mode=config.mode if config.mode in {"spam", "hold"} else "spam",
        trigger_cps=_coerce_int(config.trigger_cps, 5, 1),
        turbo_cps=_coerce_int(config.turbo_cps, 70, 1),
        stop_delay=_coerce_int(config.stop_delay, 1000, 0),
        hold_delay=_coerce_int(config.hold_delay, 200, 0),
        dbl_interval=_coerce_int(config.dbl_interval, 300, 0),
        hold_activation=config.hold_activation if config.hold_activation in {"normal", "double-click"} else "normal",
        wait_button=(config.wait_button or "").strip(),
        wait_enabled=_coerce_bool(config.wait_enabled),
        universal_enabled=_coerce_bool(config.universal_enabled),
    )
    if safe.mode == "hold" and safe.hold_activation == "double-click":
        safe.wait_enabled = False
    return safe


def load_config(profile_path: Path | str) -> AppConfig:
    path = Path(profile_path)
    if not path.exists():
        return sanitize_config(DEFAULT_CONFIG)

    parser = configparser.ConfigParser()
    parser.read(path, encoding="utf-8")
    section = "Settings"
    if not parser.has_section(section):
        return sanitize_config(DEFAULT_CONFIG)

    config = AppConfig(
        profile_name=path.stem,
        mode=parser.get(section, "mode", fallback=DEFAULT_CONFIG.mode),
        trigger_cps=parser.getint(section, "trigger_cps", fallback=DEFAULT_CONFIG.trigger_cps),
        turbo_cps=parser.getint(section, "turbo_cps", fallback=DEFAULT_CONFIG.turbo_cps),
        stop_delay=parser.getint(section, "stop_delay", fallback=DEFAULT_CONFIG.stop_delay),
        hold_delay=parser.getint(section, "hold_delay", fallback=DEFAULT_CONFIG.hold_delay),
        dbl_interval=parser.getint(section, "dbl_interval", fallback=DEFAULT_CONFIG.dbl_interval),
        hold_activation=parser.get(section, "hold_activation", fallback=DEFAULT_CONFIG.hold_activation),
        wait_button=parser.get(section, "wait_button", fallback=""),
        wait_enabled=parser.getboolean(section, "wait_enabled", fallback=False),
        universal_enabled=parser.getboolean(section, "universal_enabled", fallback=False),
    )
    return sanitize_config(config)


def save_config(profile_path: Path | str, config: AppConfig) -> None:
    path = Path(profile_path)
    path.parent.mkdir(parents=True, exist_ok=True)
    safe_config = sanitize_config(config)
    parser = configparser.ConfigParser()
    parser["Settings"] = {
        "mode": safe_config.mode,
        "trigger_cps": str(safe_config.trigger_cps),
        "turbo_cps": str(safe_config.turbo_cps),
        "stop_delay": str(safe_config.stop_delay),
        "hold_delay": str(safe_config.hold_delay),
        "dbl_interval": str(safe_config.dbl_interval),
        "hold_activation": safe_config.hold_activation,
        "wait_button": safe_config.wait_button,
        "wait_enabled": "1" if safe_config.wait_enabled else "0",
        "universal_enabled": "1" if safe_config.universal_enabled else "0",
    }
    with path.open("w", encoding="utf-8") as handle:
        parser.write(handle)


def save_runtime_state(data_dir: Path | str, *, enabled: bool, profile_name: str) -> None:
    directory = Path(data_dir)
    directory.mkdir(parents=True, exist_ok=True)
    state_path = directory / "runtime_state.ini"
    parser = configparser.ConfigParser()
    parser["Runtime"] = {
        "enabled": "1" if enabled else "0",
        "profile_name": profile_name or "default",
    }
    with state_path.open("w", encoding="utf-8") as handle:
        parser.write(handle)


def load_runtime_state(data_dir: Path | str) -> Dict[str, object]:
    directory = Path(data_dir)
    state_path = directory / "runtime_state.ini"
    if not state_path.exists():
        return {"enabled": False, "profile_name": "default"}

    parser = configparser.ConfigParser()
    parser.read(state_path, encoding="utf-8")
    if not parser.has_section("Runtime"):
        return {"enabled": False, "profile_name": "default"}

    return {
        "enabled": parser.getboolean("Runtime", "enabled", fallback=False),
        "profile_name": parser.get("Runtime", "profile_name", fallback="default"),
    }


def list_profiles(data_dir: Path | str) -> List[str]:
    directory = Path(data_dir)
    if not directory.exists():
        return ["default"]
    profiles = [path.stem for path in directory.glob("*.ini") if path.stem != "default"]
    return ["default", *sorted(profiles)]


def profile_path(data_dir: Path | str, profile_name: str) -> Path:
    directory = Path(data_dir)
    directory.mkdir(parents=True, exist_ok=True)
    return directory / f"{profile_name}.ini"
