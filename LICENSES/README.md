# Third-party license notices

CIPHER's own source code is MIT-licensed (see the repository `LICENSE`).

No third-party runtime binaries or voice models are bundled in the current Stage 1 source drop.
Before a release installer is produced, this directory must contain the exact license and notice
files for every bundled dependency, including but not limited to:

- .NET runtime components;
- Vosk/Kaldi components and the selected speech model;
- eSpeak NG and its data files;
- Playwright and any bundled browser build;
- SQLite components;
- WiX-generated installer components where notices are required;
- optional llama.cpp components if ever bundled.

Licensing must be reviewed before distribution. In particular, eSpeak NG's GPL obligations and
the license/redistribution terms of any selected Vosk model must be verified rather than assumed.
