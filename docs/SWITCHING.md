# Account switching

1. Add an account through **+**, or import the current login from the Windows **↻** right-click menu.
2. Compare the cards and select **Switch**.
3. The button shows **Backing up** while active local chat names are saved with an updated date.
4. After name verification, Codex exits normally. Windows waits for all matching desktop processes to finish, including child cleanup, before the old login is backed up and the selected login is applied. This source 1.29.1 correction prevents a premature stop that could require a second click.
5. Codex reopens the same chat IDs with their folders, models and permissions. Eligible idle continuations receive one **Continue**.

If chat naming or normal shutdown fails, the login change stops. Unclear message-send results are not automatically repeated. A pending handoff can continue through **Resume chats**; cancellation is available from the account menu/tray.

One manual **Switch** starts the normal close/apply/reopen/resume sequence. A successful Windows shutdown request has a cancellable 30-second exit deadline; refusal or timeout leaves credentials unchanged. It does not force-kill Codex or retry an uncertain continuation. [Isolated reproduction and application evidence](evidence/single-click-handoff/README.md). Current 1.27.6 downloads do not contain this correction.

Switching intentionally restarts Codex and affects other clients sharing the same login. Activity counts cover this machine's local desktop root chats, not individual accounts, remote/cloud work or standalone CLI turns.

The documentation image is a fictional dashboard demo. It is not proof of a live account switch or successful chat resumption.
