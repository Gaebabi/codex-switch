"""Request the native provider's password dialog; never handle its password."""
import os
from pathlib import Path
import sys
from gi.repository import Gio, GLib

root = Path('/home/verifier/interactive')
if os.geteuid() != 1000 or os.environ.get('CODEX_SWITCH_INTERACTIVE') != 'owner-operated' or root.is_symlink() or root.stat().st_uid != os.geteuid() or root.stat().st_mode & 0o077:
    raise SystemExit('Only the owned private verification desktop is allowed')
connection = Gio.bus_get_sync(Gio.BusType.SESSION, None)
service = 'org.freedesktop.secrets'
path = '/org/freedesktop/secrets'


def call(object_path, interface, method, arguments):
    return connection.call_sync(service, object_path, interface, method, arguments, None,
                                Gio.DBusCallFlags.NONE, 10000, None).unpack()


def alias():
    return call(path, 'org.freedesktop.Secret.Service', 'ReadAlias', GLib.Variant('(s)', ('default',)))[0]


if alias() != '/':
    # Existing keys and locked collections belong to their owner. This helper
    # neither replaces a collection nor attempts to unlock it.
    raise SystemExit(0)
collection, prompt = call(path, 'org.freedesktop.Secret.Service', 'CreateCollection',
                          GLib.Variant('(a{sv}s)', ({'org.freedesktop.Secret.Collection.Label': GLib.Variant('s', 'Linux verification login')}, 'default')))
if collection != '/':
    raise SystemExit(0)
if not prompt.startswith(path + '/prompt/'):
    raise RuntimeError('The native provider did not return a collection or password prompt')
loop = GLib.MainLoop()
completed = []


def finished(_connection, _sender, _path, _interface, _signal, parameters, _data):
    completed.append(parameters.unpack()[0])
    loop.quit()


subscription = connection.signal_subscribe(service, 'org.freedesktop.Secret.Prompt', 'Completed', prompt,
                                            None, Gio.DBusSignalFlags.NONE, finished, None)
deadline = GLib.timeout_add_seconds(600, lambda: (loop.quit(), False)[1])
try:
    # Creation and Prompt must retain the same connection. Two gdbus command
    # invocations disconnect the caller and invalidate its prompt object.
    call(prompt, 'org.freedesktop.Secret.Prompt', 'Prompt', GLib.Variant('(s)', ('',)))
    loop.run()
    if completed != [False] or alias() == '/':
        print('Native keyring setup was cancelled or remains incomplete. Use Passwords and Keys, then Refresh.', file=sys.stderr)
        raise SystemExit(2)
finally:
    connection.signal_unsubscribe(subscription)
    if GLib.MainContext.default().find_source_by_id(deadline):
        GLib.source_remove(deadline)
