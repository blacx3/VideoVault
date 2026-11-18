TEMP CLEANUP FIX

After a successful /api/file response, VideoVault deletes its job-specific
%TEMP%\videovault-<token> folder and removes the job entry.
On interrupted requests, the temp file is retained for retry.
After a successful FFmpeg merge, video.mp4 and audio.m4a are removed.
Existing old temp folders from earlier versions are not automatically removed.

The download endpoint no longer enables HTTP range responses because
a single completed transfer is required before deleting the temp file.

Build: copy ffmpeg.exe to bin, run BUILD_EXE.cmd, launch publish\VideoVault.exe.
ZIP integrity verified; Windows compilation/runtime not tested here.
