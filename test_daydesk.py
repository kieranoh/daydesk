import contextlib
import io
import json
import os
import tempfile
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import daydesk


class DaydeskTests(unittest.TestCase):
    def setUp(self):
        folder=tempfile.TemporaryDirectory(prefix="daydesk-cli-test-"); self.addCleanup(folder.cleanup)
        env=patch.dict(os.environ,{"DAYDESK_CACHE_DIR":folder.name,"DAYDESK_DATA_DIR":folder.name}); env.start(); self.addCleanup(env.stop)
        settings=patch.object(daydesk,"SETTINGS",Path(folder.name)/"settings.json"); settings.start(); self.addCleanup(settings.stop)
        executable=Path(folder.name)/'FirebaseTodo.exe'; executable.touch()
        native=patch.object(daydesk,'TODO',executable); native.start(); self.addCleanup(native.stop)
    def test_editor_opens_browser_and_reports_destination(self):
        args = daydesk.parser().parse_args(["todo", "editor", "--task", "찾을 할 일"])
        result = {"opened":"browser", "url":"https://www.todomate.net/"}
        with patch.object(daydesk, "control", return_value=result) as control:
            self.assertEqual(daydesk.run(args), result)
            control.assert_called_once_with({"op":"editor", "task":"찾을 할 일"}, launch=True)
            self.assertIn("기본 브라우저", daydesk.display(result))

    def test_invalid_settings_never_reach_widget(self):
        for args in (("settings", "--transparency", "nan"), ("settings", "--transparency", "96"),
                     ("open", "--date", "2026-02-30"), ("open", "--date", "20261103"),
                     ("open", "--view", "week"), ("settings", "--view", "week")):
            with self.subTest(args=args), patch.object(daydesk, "rpc") as rpc, contextlib.redirect_stderr(io.StringIO()) as error:
                self.assertEqual(daydesk.main([*args, "--json"]), 1)
                self.assertIn("error", json.loads(error.getvalue()))
                rpc.assert_not_called()

    def test_done_and_undo_forward_explicit_boolean_without_shell(self):
        for undo, expected in ((False, "true"), (True, "false")):
            args = daydesk.parser().parse_args(["todo", "done", "--id", "literal&ID", *(["--undo"] if undo else [])])
            with patch.object(daydesk.subprocess, "run", return_value=SimpleNamespace(returncode=0, stdout='{"success":true}')) as run, patch.object(daydesk, "rpc"):
                self.assertTrue(daydesk.todo_command(args)["success"])
                self.assertEqual(run.call_args.args[0][-4:], ["--id", "literal&ID", "--done", expected])
                self.assertFalse(run.call_args.kwargs.get("shell", False))

    def test_add_preserves_unicode_quotes_and_shell_characters(self):
        content = '한글 "인용" & $(명령)'
        args = daydesk.parser().parse_args(["todo", "add", "--goal", "연구", "--content", content, "--date", "2026-11-03"])
        with patch.object(daydesk.subprocess, "run", return_value=SimpleNamespace(returncode=0, stdout='{"id":"fake"}')) as run, patch.object(daydesk, "rpc"):
            self.assertEqual(daydesk.todo_command(args)["id"], "fake")
            self.assertIn(content, run.call_args.args[0])

    def test_invalid_reminder_never_writes(self):
        args = daydesk.parser().parse_args(["todo", "remind", "--id", "fake", "--at", "2026-11-03T99:99"])
        with patch.object(daydesk.subprocess, "run") as run:
            with self.assertRaises(ValueError):
                daydesk.todo_command(args)
            run.assert_not_called()

    def test_closed_status_uses_saved_settings(self):
        with patch.object(daydesk, "rpc", side_effect=FileNotFoundError), patch.object(daydesk, "saved", return_value={"view":"week", "background_transparency":45}), patch.object(daydesk, "startup", return_value={"autostart":True}):
            result = daydesk.run(daydesk.parser().parse_args(["status"]))
            self.assertFalse(result["running"])
            self.assertEqual(result["view"], "month")
            self.assertEqual(result["transparency"], 45)

    def test_failed_remote_write_does_not_refresh_or_retry(self):
        args = daydesk.parser().parse_args(["todo", "add", "--goal", "연구", "--content", "검증"])
        with patch.object(daydesk.subprocess, "run", return_value=SimpleNamespace(returncode=1, stderr='{"error":"등록 여부를 확인해 주세요"}')) as run, patch.object(daydesk, "rpc") as rpc:
            with self.assertRaisesRegex(ValueError, "등록 여부"):
                daydesk.todo_command(args)
            self.assertEqual(run.call_count, 1)
            rpc.assert_not_called()

    def test_ambiguous_calendar_rejected_and_connection_closed(self):
        args = daydesk.parser().parse_args(["timetree", "events"])
        with patch.object(daydesk, "TimeTree") as Client, patch.object(daydesk, "saved", return_value={}):
            client = Client.return_value
            client.call.return_value = {"calendars":[{"id":"a", "name":"A"},{"id":"b", "name":"B"}]}
            with self.assertRaisesRegex(ValueError, "하나로"):
                daydesk.timetree_command(args)
            client.close.assert_called_once()

    def test_settings_apply_one_validated_command(self):
        args = daydesk.parser().parse_args(["settings", "--transparency", "45", "--pin", "off"])
        with patch.object(daydesk, "control", return_value={}) as control:
            daydesk.run(args)
            control.assert_called_once_with({"op":"settings", "transparency":45.0, "pin":False}, launch=True)

    def test_pane_width_and_recommended_settings(self):
        for arguments,request in ((["--todo-width","400"],{"op":"settings","todo_width":400}),(["--recommended"],{"op":"settings","recommended":True})):
            with patch.object(daydesk,"control",return_value={}) as control:
                daydesk.run(daydesk.parser().parse_args(["settings",*arguments]))
                control.assert_called_once_with(request,launch=True)
        with patch.object(daydesk,"control") as control:
            with self.assertRaises(ValueError):
                daydesk.run(daydesk.parser().parse_args(["settings","--recommended","--todo-width","400"]))
            control.assert_not_called()


if __name__ == "__main__":
    unittest.main()
