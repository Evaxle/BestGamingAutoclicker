import io
import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from autoclicker_config import (
    AppConfig,
    list_profiles,
    load_config,
    load_runtime_state,
    save_config,
    save_runtime_state,
    validate_settings,
)


class ConfigTests(unittest.TestCase):
    def test_validate_settings_rejects_invalid_values(self):
        config = AppConfig(
            mode="spam",
            trigger_cps=0,
            turbo_cps=0,
            stop_delay=-1,
            hold_delay=-5,
            dbl_interval=-1,
            hold_activation="normal",
            wait_button="",
            wait_enabled=False,
            universal_enabled=False,
        )

        errors = validate_settings(config)
        self.assertIn("trigger_cps", errors)
        self.assertIn("turbo_cps", errors)
        self.assertIn("stop_delay", errors)
        self.assertIn("hold_delay", errors)
        self.assertIn("dbl_interval", errors)

    def test_save_and_load_config_round_trip(self):
        with tempfile.TemporaryDirectory() as tmp_dir:
            config = AppConfig(mode="hold", trigger_cps=6, turbo_cps=45)
            profile_path = Path(tmp_dir) / "default.ini"
            save_config(profile_path, config)

            loaded = load_config(profile_path)
            self.assertEqual(loaded.mode, "hold")
            self.assertEqual(loaded.trigger_cps, 6)
            self.assertEqual(loaded.turbo_cps, 45)

    def test_runtime_state_round_trip(self):
        with tempfile.TemporaryDirectory() as tmp_dir:
            save_runtime_state(Path(tmp_dir), enabled=True, profile_name="test")
            state = load_runtime_state(Path(tmp_dir))
            self.assertTrue(state["enabled"])
            self.assertEqual(state["profile_name"], "test")

    def test_list_profiles_ignores_runtime_state_file(self):
        with tempfile.TemporaryDirectory() as tmp_dir:
            data_dir = Path(tmp_dir)
            save_runtime_state(data_dir, enabled=True, profile_name="demo")
            save_config(data_dir / "demo.json", AppConfig(mode="hold", trigger_cps=8))

            profiles = list_profiles(data_dir)
            self.assertIn("default", profiles)
            self.assertIn("demo", profiles)
            self.assertNotIn("runtime_state", profiles)

    def test_profile_data_is_stored_as_json(self):
        with tempfile.TemporaryDirectory() as tmp_dir:
            profile_path = Path(tmp_dir) / "demo.json"
            save_config(profile_path, AppConfig(mode="hold", trigger_cps=8))

            self.assertTrue(profile_path.exists())
            payload = json.loads(profile_path.read_text(encoding="utf-8"))
            self.assertEqual(payload["Settings"]["mode"], "hold")
            self.assertEqual(payload["Settings"]["trigger_cps"], "8")

    def test_windows_launcher_script_exists(self):
        launcher_path = Path(__file__).resolve().parent.parent / "run_gui.bat"
        self.assertTrue(launcher_path.exists())
        content = launcher_path.read_text(encoding="utf-8")
        self.assertIn("gui.py", content)
        self.assertIn("requirements.txt", content)
        self.assertIn("-3w", content)

    def test_main_exits_cleanly_without_display(self):
        import gui

        with patch.dict(os.environ, {"DISPLAY": ""}, clear=False):
            with patch("sys.stdout", new_callable=io.StringIO) as stdout:
                exit_code = gui.main()

        self.assertEqual(exit_code, 1)
        self.assertIn("display", stdout.getvalue().lower())


if __name__ == "__main__":
    unittest.main()
