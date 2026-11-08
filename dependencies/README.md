# Bundled dependency staging area

This directory is used to stage and verify offline dependencies before they are included in the
WiX installer. Large binaries and models are **not** committed to ordinary Git history.

Nothing in this directory is currently a completed offline dependency bundle. The release process
must populate and verify the following layout before `CIPHER.msi` can be called complete:

```text
dependencies/
  manifest.sha256
  vosk/
    model-en-us-small/...
    native/win-x64/...
  espeak-ng/
    bin/...
    espeak-ng-data/...
  playwright/
    browsers/...
  licenses/
    ...
```

`install-dependencies.bat` fails closed when the manifest or required payloads are missing. It
never downloads dependencies during offline installation.
