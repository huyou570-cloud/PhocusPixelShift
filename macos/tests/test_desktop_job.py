import contextlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('desktop_job', Path(__file__).resolve().parents[1] / 'mac-app/job.py')
job = importlib.util.module_from_spec(spec)
spec.loader.exec_module(job)


class DesktopJobTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        job.cancelled = False
        job.active = None

    def test_reject_sixteen_without_creating_outputs(self):
        paths = []
        for i in range(16):
            p = self.root / f'{i}.3FR'
            p.write_bytes(b'fixture')
            paths.append(str(p))
        config = self.root / 'job.json'
        config.write_text(json.dumps({'inputs': paths, 'output': str(self.root / 'output')}))
        with self.assertRaises(ValueError):
            job.run(config)
        self.assertFalse((self.root / 'output').exists())

    def test_cancel_stops_child_process(self):
        cancel = self.root / 'cancel.request'
        cancel.touch()
        with self.assertRaisesRegex(RuntimeError, '任务已停止'):
            job.command([sys.executable, '-c', 'import time;time.sleep(60)'], self.root / 'log', cancel)
        self.assertIsNotNone(job.active.poll())

    def test_timeout_stops_child_process(self):
        with self.assertRaisesRegex(RuntimeError, '处理超时'):
            job.command([sys.executable, '-c', 'import time;time.sleep(60)'], self.root / 'log', self.root / 'cancel', timeout=.1)
        self.assertIsNotNone(job.active.poll())

    def test_signal_requests_cooperative_cancellation(self):
        process = type('Process', (), {'poll': lambda self: None, 'pid': 12345})()
        job.active = process
        with patch.object(job.os, 'killpg') as kill:
            job.stop(None, None)
        self.assertTrue(job.cancelled)
        kill.assert_not_called()


if __name__ == '__main__':
    unittest.main()
