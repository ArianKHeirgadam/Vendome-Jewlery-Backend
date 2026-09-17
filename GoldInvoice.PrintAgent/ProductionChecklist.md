# Production checklist

- Windows runner builds the solution in CI.
- Agent state is protected with Windows DPAPI CurrentUser.
- Device requests are signed with the enrolled RSA private key.
- One PrintAgent instance is enforced per Windows user.
- Device heartbeat is signed and sent periodically.
- Local printer existence and spooler status are checked before print submission.
- Configured paper size is checked against Windows printer capabilities when capabilities are available.
- WebView2 print success is reported only as Windows print-pipeline acceptance.
- Physical printer acceptance remains required for paper output, tray selection, media, orientation, color and copy count.
