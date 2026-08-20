# YouTube Audio Downloading

This context describes how requested YouTube audio downloads are grouped, executed, and tracked by the desktop application.

## Language

**Download item**:
A request to extract one MP3 file from one YouTube URL, including its lifecycle state and progress.
_Avoid_: Job, task, queue entry

**Download batch**:
The set of eligible download items coordinated by one start operation, including items added while that operation remains active.
_Avoid_: Session, run, queue

**Download attempt**:
One execution of a download item through `yt-dlp`, ending in completion, failure, or cancellation.
_Avoid_: Process, invocation
