CLEAN SHUTDOWN PATCH
Close with X, Alt+F4, Ctrl+Q or the Exit menu.
Window close now awaits stopping and disposing the ASP.NET Core server.
This releases the listener on 127.0.0.1:3000 owned by VideoVault.
When started via RUN_DESKTOP.cmd, the dotnet run command then returns
and the batch window exits. The EXE has no separate server process.
This patch does NOT kill unrelated processes using port 3000.

Build: copy ffmpeg.exe to bin, run BUILD_EXE.cmd, then
launch publish\VideoVault.exe.
This ZIP was validated, but Windows compilation was not available.
