# Audio generators

Offline renderers for every shipped clip (art bible §8). The outputs are committed in
`Assets/Resources/Audio/`; re-run a generator only when you change it.

```bash
python3 -m venv .venv-audio && .venv-audio/bin/pip install -r tools/audio/requirements.txt
.venv-audio/bin/python tools/audio/ionmusic.py            # tutorial, hub, rooms -> Resources/Audio/Music
.venv-audio/bin/python tools/audio/ionsfx.py              # every SFX + footsteps -> Resources/Audio/Sfx
.venv-audio/bin/python tools/audio/ionsfx.py photo_place  # just one
```

- `ionlib.py`: shared DSP: instruments, the 3.6 s room reverb, the tape wobble, hiss and crackle, loudness, Ogg output.
- `ionmusic.py`: seamless loops in D lydian at about -20 LUFS, 32 kHz stereo. Each loop's reverb and note tails wrap around.
- `ionsfx.py`: mono 44.1 kHz SFX that peak at -6 dBFS. Clips ending in `_loop` are exact loops.
- `sources/kenney_impact_sounds/`: the CC0 footsteps and impact layers (Kenney "Impact Sounds"), unmodified. See `Assets/Resources/Audio/CREDITS.txt`.

Everything is seeded and deterministic. Unity re-encodes the clips for the web build (AAC); see
`Assets/Scripts/Presentation/Audio/Editor/IonAudioImporter.cs` for the import settings and the size budget.
