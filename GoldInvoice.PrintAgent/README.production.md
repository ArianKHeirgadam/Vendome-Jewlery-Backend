# PrintAgent production acceptance

The agent performs signed device authentication, single-instance protection, periodic heartbeat, printer existence/status preflight, configured paper-size capability checks, and backend print-job completion reporting.

Physical acceptance must still verify the actual printer: correct device, paper tray/media, orientation, color mode, number of copies, and behavior for offline/out-of-paper/jam conditions. WebView2 success means the Windows print pipeline accepted the job; it is not proof that paper physically exited the printer.
