# AZ Jarvis for Android

Jarvis is a native Jetpack Compose Android assistant with the purple interface inspired by the supplied mobile reference. Windows development is paused.

## Available Android features

- Onboarding, dashboard, chat, full-screen voice view, history, settings, quick actions, and purple glass-style visuals.
- Text chat with an OpenAI-compatible HTTPS provider configured by the user. Responses stream in the app; the API key and history are encrypted with Android Keystore-backed storage.
- Push-to-talk using Android speech recognition and spoken replies using installed text-to-speech voices.
- Local commands for battery, volume, flashlight, media, app launch, settings, alarms, and accessibility actions. Accessibility and notification reading require explicit user permission.
- Optional offline English “Hey Jarvis” detection with Vosk. The microphone service displays a persistent notification while active. The model download is user initiated and verified by SHA-256 before installation. The model is [Vosk small English 0.15](https://alphacephei.com/vosk/models), Apache 2.0.
- Release update checks with SHA-256 verification and Android's normal installation confirmation.

## Build and installation

GitHub Actions runs Android unit tests, builds a signed release APK, verifies its signing certificate against the previous release, and publishes `Jarvis-Android.apk` from `main`. The signing keystore is reused; the workflow fails if it cannot restore that key. Install the APK through Android's standard sideload confirmation. Grant microphone, camera/flashlight, accessibility, or notification permissions only for features you choose to use.

## Current limits

This is a development release. The offline wake loop has compiled in CI but still needs a microphone test on the Nubia V70 Max. Android cannot restart microphone listening after force-stop or permission revocation until the user opens or enables it again. Tamil and Sinhala speech depend on compatible installed engines and models. Tasks, calendar, long-term memory, advanced agents, and some requested integrations are still pending. Do not assume those features are active.
