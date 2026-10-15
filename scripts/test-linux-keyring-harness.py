"""Check owned-provider cleanup failure paths without starting a native service."""
import ast
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import Mock
import subprocess

tree=ast.parse(Path(__file__).with_name('verify-linux-keyring.py').read_text(encoding='utf-8'))
stop=next(node for node in tree.body if isinstance(node,ast.FunctionDef) and node.name=='stop')
code=compile(ast.Module(body=[stop],type_ignores=[]),'owned-provider-cleanup','exec')

class ProviderCleanup(unittest.TestCase):
    def environment(self):
        process=Mock();process.poll.return_value=None
        state={'daemon':process,'a':SimpleNamespace(provider='kwallet5'),'verify_owner':Mock(return_value=':1.1'),
            'dbus':Mock(return_value=SimpleNamespace(stdout='(0,)')),'ready':Mock(),'subprocess':subprocess}
        exec(code,state)
        return state,process

    def test_identity_failure_still_terminates_owned_process(self):
        state,process=self.environment();state['verify_owner'].side_effect=RuntimeError('identity failure')
        with self.assertRaisesRegex(RuntimeError,'identity failure'):state['stop']()
        process.terminate.assert_called_once();process.wait.assert_called_once_with(timeout=10)

    def test_persistence_failure_still_terminates_and_reports_failure(self):
        state,process=self.environment();state['dbus'].return_value.stdout='(-1,)'
        with self.assertRaisesRegex(RuntimeError,'persist and close'):state['stop']()
        process.terminate.assert_called_once();process.wait.assert_called_once_with(timeout=10)

    def test_failed_start_cleanup_does_not_require_a_ready_wallet(self):
        state,process=self.environment();state['stop'](persist=False)
        state['verify_owner'].assert_not_called();state['dbus'].assert_not_called()
        process.terminate.assert_called_once();self.assertIsNone(state['daemon'])

    def test_owned_process_shutdown_has_bounded_kill_fallback(self):
        state,process=self.environment();process.wait.side_effect=[subprocess.TimeoutExpired('owned-provider',10),0]
        state['stop'](persist=False)
        process.kill.assert_called_once();self.assertEqual(process.wait.call_count,2)

if __name__=='__main__':unittest.main()
