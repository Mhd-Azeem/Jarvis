package com.azeem.jarvis.core

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.speech.tts.TextToSpeech
import android.speech.tts.UtteranceProgressListener
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.azeem.jarvis.MainActivity
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject
import org.vosk.Model
import org.vosk.Recognizer
import org.vosk.android.RecognitionListener
import org.vosk.android.SpeechService
import java.io.File
import java.util.Locale

object WakeUiState {
    var status by mutableStateOf("Hey Jarvis is off")
    var lastReply by mutableStateOf("")
    var enabled by mutableStateOf(false)
}

class JarvisWakeService : Service() {
    companion object { const val ACTION_STOP = "com.azeem.jarvis.STOP_WAKE" }
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private val handler = Handler(Looper.getMainLooper())
    private lateinit var secure: SecureStore
    private lateinit var commands: JarvisCommandProcessor
    private var model: Model? = null
    private var speech: SpeechService? = null
    private var recognizer: Recognizer? = null
    private var tts: TextToSpeech? = null
    private var generation = 0
    private var lastWake = 0L
    private var phase = "starting"
    private var stopped = false

    override fun onCreate() {
        super.onCreate()
        secure = SecureStore(this)
        commands = JarvisCommandProcessor(this)
        tts = TextToSpeech(this) { status ->
            if (status == TextToSpeech.SUCCESS) tts?.language = Locale.getDefault()
        }
        tts?.setOnUtteranceProgressListener(object : UtteranceProgressListener() {
            override fun onStart(utteranceId: String?) = Unit
            override fun onDone(utteranceId: String?) { handler.post { if (!stopped) startWake() } }
            @Deprecated("Deprecated in Java")
            override fun onError(utteranceId: String?) { handler.post { if (!stopped) startWake() } }
        })
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            secure.put("wake_enabled", "false")
            stopSelf()
            return START_NOT_STICKY
        }
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED ||
            !WakeModel(this).installed()) {
            WakeUiState.status = "Grant microphone access and install the offline model first."
            stopSelf()
            return START_NOT_STICKY
        }
        try {
            foreground("Preparing offline wake listener")
        } catch (error: Exception) {
            WakeUiState.status = error.message ?: "Android blocked microphone service startup."
            stopSelf()
            return START_NOT_STICKY
        }
        if (model == null) scope.launch(Dispatchers.IO) {
            try {
                val loaded = Model(File(filesDir, "vosk-model-small-en-us-0.15").absolutePath)
                handler.post { model = loaded; if (!stopped) startWake() }
            } catch (error: Exception) {
                handler.post {
                    WakeUiState.status = error.message ?: "Could not load the offline speech model."
                    stopSelf()
                }
            }
        }
        return START_NOT_STICKY
    }

    private fun foreground(message: String) {
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel("jarvis_wake", "Jarvis microphone", NotificationManager.IMPORTANCE_LOW))
        val openIntent = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val stopIntent = PendingIntent.getService(this, 1, Intent(this, JarvisWakeService::class.java).setAction(ACTION_STOP), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val notification = NotificationCompat.Builder(this, "jarvis_wake")
            .setSmallIcon(android.R.drawable.ic_btn_speak_now)
            .setContentTitle("Jarvis offline voice")
            .setContentText(message)
            .setContentIntent(openIntent)
            .addAction(android.R.drawable.ic_delete, "Stop listening", stopIntent)
            .setOngoing(true)
            .build()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q)
            startForeground(901, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE)
        else startForeground(901, notification)
        WakeUiState.status = message
        WakeUiState.enabled = true
    }

    private fun stopRecognizer() {
        generation++
        speech?.cancel()
        speech?.shutdown()
        speech = null
        recognizer?.close()
        recognizer = null
    }

    private fun startWake() {
        if (stopped || model == null) return
        stopRecognizer()
        phase = "wake"
        try {
            recognizer = Recognizer(model, 16000f, "[\"hey jarvis\", \"[unk]\"]").apply { setWords(true) }
            speech = SpeechService(recognizer, 16000f)
            val token = generation
            speech?.startListening(listener(token))
            foreground("Listening locally for Hey Jarvis")
        } catch (error: Exception) {
            WakeUiState.status = error.message ?: "Microphone is unavailable."
            stopSelf()
        }
    }

    private fun startCommand() {
        stopRecognizer()
        phase = "command"
        try {
            recognizer = Recognizer(model, 16000f)
            speech = SpeechService(recognizer, 16000f)
            val token = generation
            speech?.startListening(listener(token), 12_000)
            foreground("Hey Jarvis heard — listening for your request")
        } catch (error: Exception) {
            WakeUiState.status = error.message ?: "Could not capture the request."
            startWake()
        }
    }

    private fun listener(token: Int) = object : RecognitionListener {
        override fun onResult(hypothesis: String?) { if (token == generation) handle(hypothesis) }
        override fun onFinalResult(hypothesis: String?) { if (token == generation) handle(hypothesis) }
        override fun onPartialResult(hypothesis: String?) = Unit
        override fun onError(error: Exception?) {
            if (token == generation) {
                WakeUiState.status = error?.message ?: "Microphone error."
                stopSelf()
            }
        }
        override fun onTimeout() { if (token == generation && phase == "command") startWake() }
    }

    private fun handle(hypothesis: String?) {
        val json = runCatching { JSONObject(hypothesis ?: "{}") }.getOrNull() ?: return
        val text = json.optString("text").trim()
        if (phase == "wake") {
            val words = json.optJSONArray("result")
            var confidence = 0.0
            if (words != null && words.length() > 0) {
                for (i in 0 until words.length()) confidence += words.optJSONObject(i)?.optDouble("conf", 0.0) ?: 0.0
                confidence /= words.length()
            }
            val threshold = secure.get("wake_sensitivity")?.toDoubleOrNull() ?: 0.55
            if (text == "hey jarvis" && confidence >= threshold && System.currentTimeMillis() - lastWake > 3500) {
                lastWake = System.currentTimeMillis()
                startCommand()
            }
        } else if (phase == "command" && text.isNotBlank()) {
            stopRecognizer()
            phase = "answering"
            foreground("Processing your request")
            scope.launch {
                val answer = if (JarvisCommandProcessor.supports(text)) commands.execute(text)
                else {
                    val key = secure.get("api_key").orEmpty()
                    if (key.isBlank()) "Configure an AI provider key in Settings for this request."
                    else try {
                        val turns = readHistory() + ChatTurn("user", text)
                        AiClient().reply(
                            secure.get("endpoint") ?: "https://api.openai.com/v1/chat/completions",
                            key, secure.get("model") ?: "gpt-4o-mini", turns
                        ) { }
                    } catch (error: Exception) { error.message ?: "The provider request failed." }
                }
                appendHistory(text, answer)
                WakeUiState.lastReply = answer
                WakeUiState.status = "Jarvis: $answer"
                val result = tts?.speak(answer, TextToSpeech.QUEUE_FLUSH, null, "jarvis-wake-reply")
                if (result != TextToSpeech.SUCCESS) handler.postDelayed({ if (!stopped) startWake() }, 1500)
            }
        }
    }

    private fun readHistory(): List<ChatTurn> = runCatching {
        val array = JSONArray(secure.get("history") ?: "[]")
        (0 until array.length()).map {
            val item = array.getJSONObject(it)
            ChatTurn(item.getString("role"), item.getString("text"))
        }
    }.getOrDefault(emptyList())

    private fun appendHistory(request: String, answer: String) {
        val array = JSONArray()
        (readHistory() + ChatTurn("user", request) + ChatTurn("assistant", answer)).takeLast(100).forEach {
            array.put(JSONObject().put("role", it.role).put("text", it.text))
        }
        secure.put("history", array.toString())
    }

    override fun onDestroy() {
        stopped = true
        stopRecognizer()
        model?.close()
        tts?.shutdown()
        scope.cancel()
        WakeUiState.status = "Hey Jarvis is off"
        WakeUiState.enabled = false
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null
}
