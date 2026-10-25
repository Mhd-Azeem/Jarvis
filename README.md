# AZ Jarvis

Jarvis is a native Jetpack Compose Android assistant with the purple interface inspired by the supplied mobile reference. Windows development has resumed with an independent build.

## Available Android features

- Onboarding, dashboard, chat, full-screen voice view, history, settings, quick actions, and purple glass-style visuals.
- Text chat with an OpenAI-compatible HTTPS provider configured by the user. Responses stream in the app; the API key and history are encrypted with Android Keystore-backed storage.
- Push-to-talk using Android speech recognition and spoken replies using installed text-to-speech voices.
- Local commands for battery, volume, flashlight, media, app launch, settings, alarms, and accessibility actions. Accessibility and notification reading require explicit user permission.
- Optional offline English “Hey Jarvis” detection with Vosk. The microphone service displays a persistent notification while active. The model download is user initiated and verified by SHA-256 before installation. The model is [Vosk small English 0.15](https://alphacephei.com/vosk/models), Apache 2.0.
- Release update checks with SHA-256 verification and Android's normal installation confirmation.

## Build and installation

GitHub Actions runs Android unit tests, builds a signed release APK, verifies its signing certificate against the previous release, and publishes `Jarvis-Android.apk` from `main`. The signing keystore is reused; the workflow fails if it cannot restore that key. Install the APK through Android's standard sideload confirmation. Grant microphone, camera/flashlight, accessibility, or notification permissions only for features you choose to use.

## Windows preview

The Windows x64 app uses a dark cyan command-center dashboard with live system status, tray operation, text and push-to-talk commands, OpenAI-compatible streaming chat, local encrypted conversation history and provider settings, media and volume controls, screenshots, tasks, reminders, and optional offline English Hey Jarvis using Vosk. The model is downloaded only when requested and verified by SHA-256. Windows preview releases contain a self-contained `Jarvis-Windows.exe` built and startup-checked by a separate GitHub Actions workflow. Windows updates inspect releases for a newer EXE and verify its published SHA-256 digest. The EXE is currently unsigned.

## Current limits

This is a development release. The offline wake loop has compiled in CI but still needs a microphone test on the Nubia V70 Max. Android cannot restart microphone listening after force-stop or permission revocation until the user opens or enables it again. Tamil and Sinhala speech depend on compatible installed engines and models. Android tasks, calendar, long-term memory, advanced agents, and some requested integrations are still pending. Windows has local tasks and reminders, but calendar integration, advanced agents and broader automation are still pending. The offline Windows microphone path has not yet been checked on a physical microphone. Do not assume those features are active.
