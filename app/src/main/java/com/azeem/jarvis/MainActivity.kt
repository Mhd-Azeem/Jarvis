package com.azeem.jarvis

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.provider.Settings
import android.os.Bundle
import android.os.Build
import android.speech.RecognizerIntent
import android.speech.tts.TextToSpeech
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Slider
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.lifecycleScope
import androidx.core.content.ContextCompat
import com.azeem.jarvis.core.AiClient
import com.azeem.jarvis.core.ChatTurn
import com.azeem.jarvis.core.JarvisCommandProcessor
import com.azeem.jarvis.core.SecureStore
import com.azeem.jarvis.core.WakeModel
import com.azeem.jarvis.core.WakeUiState
import com.azeem.jarvis.core.JarvisWakeService
import com.azeem.jarvis.update.UpdateManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.util.Locale

private val Purple = Color(0xFFB45AFF)
private val Pink = Color(0xFFF457D5)
private val Background = Color(0xFF080413)
private val Panel = Color(0xCC1A0D2D)
private val TextColor = Color(0xFFF7EDFF)
private val Dim = Color(0xFFAB97C0)
private val Glow = Brush.linearGradient(listOf(Purple, Pink))

private enum class Page { Welcome, Home, Chat, Voice, History, Settings }

class MainActivity : ComponentActivity(), TextToSpeech.OnInitListener {
    private lateinit var commands: JarvisCommandProcessor
    private lateinit var secure: SecureStore
    private lateinit var updates: UpdateManager
    private var tts: TextToSpeech? = null

    private var page by mutableStateOf(Page.Home)
    private var prompt by mutableStateOf("")
    private var response by mutableStateOf("Ready when you are.")
    private var turns by mutableStateOf<List<ChatTurn>>(emptyList())
    private var busy by mutableStateOf(false)
    private var listening by mutableStateOf(false)
    private var apiKey by mutableStateOf("")
    private var endpoint by mutableStateOf("https://api.openai.com/v1/chat/completions")
    private var model by mutableStateOf("gpt-4o-mini")
    private var speakReplies by mutableStateOf(true)
    private var updateStatus by mutableStateOf("Tap to check for updates")
    private var availableUpdate by mutableStateOf<UpdateManager.AvailableUpdate?>(null)
    private var requestCameraPermission: (() -> Unit)? = null
    private var pendingCameraCommand: String? = null
    private var requestMicrophonePermission: (() -> Unit)? = null
    private var requestNotificationPermission: (() -> Unit)? = null
    private var wakeInstalled by mutableStateOf(false)
    private var wakeInstalling by mutableStateOf(false)
    private var wakeInstallStatus by mutableStateOf("Model not installed")
    private var wakeSensitivity by mutableStateOf(0.55f)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        commands = JarvisCommandProcessor(applicationContext)
        secure = SecureStore(applicationContext)
        updates = UpdateManager(this)
        tts = TextToSpeech(this, this)
        apiKey = secure.get("api_key").orEmpty()
        endpoint = secure.get("endpoint") ?: endpoint
        model = secure.get("model") ?: model
        speakReplies = secure.get("speak") != "false"
        turns = readHistory()
        wakeInstalled = WakeModel(this).installed()
        wakeInstallStatus = if (wakeInstalled) "Offline model ready" else "Download the English offline model before enabling wake." 
        wakeSensitivity = secure.get("wake_sensitivity")?.toFloatOrNull() ?: 0.55f
        page = if (secure.get("onboarded") == "yes") Page.Home else Page.Welcome

        setContent {
            val cameraLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
                val pending = pendingCameraCommand
                pendingCameraCommand = null
                if (granted && pending != null) submit(pending)
                else response = "Camera permission is required to control the flashlight."
            }
            requestCameraPermission = { cameraLauncher.launch(Manifest.permission.CAMERA) }
            val microphoneLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
                if (granted) startWakeService() else wakeInstallStatus = "Microphone permission was denied."
            }
            requestMicrophonePermission = { microphoneLauncher.launch(Manifest.permission.RECORD_AUDIO) }
            val notificationLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
                if (granted) startWakeService() else wakeInstallStatus = "Notification permission is required for persistent listening."
            }
            requestNotificationPermission = { notificationLauncher.launch(Manifest.permission.POST_NOTIFICATIONS) }
            val launcher = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
                listening = false
                val heard = result.data?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)?.firstOrNull()
                if (heard.isNullOrBlank()) {
                    response = "I didn't catch that. Tap the microphone to try again."
                } else {
                    prompt = heard
                    submit(heard)
                }
            }
            MaterialTheme(colorScheme = darkColorScheme(primary = Purple, secondary = Pink, background = Background, surface = Panel, onBackground = TextColor, onSurface = TextColor)) {
                JarvisApp(
                    page = page,
                    onPage = { page = it },
                    prompt = prompt,
                    onPrompt = { prompt = it },
                    response = response,
                    turns = turns,
                    busy = busy,
                    listening = listening,
                    connected = apiKey.isNotBlank(),
                    endpoint = endpoint,
                    onEndpoint = { endpoint = it; secure.put("endpoint", it) },
                    model = model,
                    onModel = { model = it; secure.put("model", it) },
                    apiKey = apiKey,
                    onApiKey = { apiKey = it; secure.put("api_key", it) },
                    speakReplies = speakReplies,
                    onSpeakReplies = { speakReplies = it; secure.put("speak", it.toString()) },
                    updateStatus = updateStatus,
                    wakeInstalled = wakeInstalled,
                    wakeInstalling = wakeInstalling,
                    wakeInstallStatus = wakeInstallStatus,
                    wakeStatus = WakeUiState.status,
                    wakeEnabled = WakeUiState.enabled,
                    wakeSensitivity = wakeSensitivity,
                    onStart = { secure.put("onboarded", "yes"); page = Page.Home },
                    onInstallWakeModel = ::installWakeModel,
                    onWakeToggle = { enabled -> if (enabled) startWakeService() else stopWakeService() },
                    onWakeSensitivity = { wakeSensitivity = it; secure.put("wake_sensitivity", it.toString()) },
                    onSubmit = { submit(prompt) },
                    onListen = {
                        if (WakeUiState.enabled) {
                            response = "Turn off Hey Jarvis before using push-to-talk."
                        } else {
                            listening = true
                            page = Page.Voice
                            try {
                                launcher.launch(Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
                                    putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
                                    putExtra(RecognizerIntent.EXTRA_LANGUAGE, Locale.getDefault())
                                    putExtra(RecognizerIntent.EXTRA_PROMPT, "Speak with Jarvis")
                                })
                            } catch (_: Exception) {
                                listening = false
                                response = "No speech recognition service is available on this device."
                            }
                        }
                    },
                    onQuick = { submit(it) },
                    onClearHistory = { turns = emptyList(); secure.remove("history") },
                    onCheckUpdate = ::checkUpdate,
                    onInstallUpdate = ::installUpdate,
                    onAccessibilitySettings = { startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS)) },
                    onNotificationSettings = { startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)) }
                )
            }
        }
    }

    private fun installWakeModel() {
        if (wakeInstalling || wakeInstalled) return
        wakeInstalling = true
        lifecycleScope.launch {
            wakeInstallStatus = WakeModel(this@MainActivity).install { message ->
                withContext(Dispatchers.Main) { wakeInstallStatus = message }
            }
            wakeInstalled = WakeModel(this@MainActivity).installed()
            wakeInstalling = false
        }
    }

    private fun startWakeService() {
        if (!WakeModel(this).installed()) {
            wakeInstallStatus = "Install the offline model first."
            return
        }
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            requestMicrophonePermission?.invoke()
            return
        }
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestNotificationPermission?.invoke()
            return
        }
        try {
            ContextCompat.startForegroundService(this, Intent(this, JarvisWakeService::class.java))
            secure.put("wake_enabled", "true")
        } catch (error: Exception) {
            wakeInstallStatus = error.message ?: "Android blocked microphone service startup."
        }
    }

    private fun stopWakeService() {
        stopService(Intent(this, JarvisWakeService::class.java))
        secure.put("wake_enabled", "false")
    }
    private fun submit(input: String) {
        val value = input.trim()
        if (value.isEmpty() || busy) return
        if ((value.contains("flashlight", true) || value.contains("torch", true)) &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            pendingCameraCommand = value
            requestCameraPermission?.invoke()
            return
        }
        prompt = ""
        response = ""
        val request = turns + ChatTurn("user", value)
        turns = request
        saveHistory()
        if (JarvisCommandProcessor.supports(value)) {
            response = commands.execute(value)
            turns = turns + ChatTurn("assistant", response)
            saveHistory()
            speak(response)
            return
        }
        if (apiKey.isBlank()) {
            response = "This needs an AI provider. Add an API key in Settings. Device commands work offline."
            turns = turns + ChatTurn("assistant", response)
            saveHistory()
            return
        }
        busy = true
        lifecycleScope.launch {
            try {
                val answer = AiClient().reply(endpoint, apiKey, model, request) { chunk ->
                    withContext(Dispatchers.Main) { response = chunk }
                }
                response = answer
                turns = turns + ChatTurn("assistant", answer)
                saveHistory()
                speak(answer)
            } catch (error: Exception) {
                response = error.message ?: "The provider request failed."
                turns = turns + ChatTurn("assistant", response)
                saveHistory()
            } finally {
                busy = false
            }
        }
    }

    private fun readHistory(): List<ChatTurn> = runCatching {
        val json = JSONArray(secure.get("history") ?: "[]")
        (0 until json.length()).map { index ->
            val turn = json.getJSONObject(index)
            ChatTurn(turn.getString("role"), turn.getString("text"))
        }
    }.getOrDefault(emptyList())

    private fun saveHistory() {
        val array = JSONArray()
        turns.takeLast(100).forEach { array.put(JSONObject().put("role", it.role).put("text", it.text)) }
        secure.put("history", array.toString())
    }

    private fun speak(value: String) {
        if (speakReplies) tts?.speak(value, TextToSpeech.QUEUE_FLUSH, null, "jarvis-reply")
    }

    private fun checkUpdate() {
        if (busy) return
        busy = true
        updateStatus = "Checking GitHub…"
        lifecycleScope.launch {
            when (val result = updates.checkForUpdate()) {
                is UpdateManager.CheckResult.Available -> {
                    availableUpdate = result.update
                    updateStatus = "Build #${result.update.buildNumber} is available. Tap Install."
                }
                is UpdateManager.CheckResult.Current -> updateStatus = "You have the latest release."
                is UpdateManager.CheckResult.Error -> updateStatus = result.message
            }
            busy = false
        }
    }

    private fun installUpdate() {
        val update = availableUpdate ?: return
        if (busy) return
        busy = true
        lifecycleScope.launch {
            updateStatus = updates.downloadAndInstall(update)
            busy = false
        }
    }

    override fun onInit(status: Int) {
        if (status == TextToSpeech.SUCCESS) tts?.language = Locale.getDefault()
    }

    override fun onDestroy() {
        tts?.shutdown()
        super.onDestroy()
    }
}

@Composable
private fun JarvisApp(
    page: Page,
    onPage: (Page) -> Unit,
    prompt: String,
    onPrompt: (String) -> Unit,
    response: String,
    turns: List<ChatTurn>,
    busy: Boolean,
    listening: Boolean,
    connected: Boolean,
    endpoint: String,
    onEndpoint: (String) -> Unit,
    model: String,
    onModel: (String) -> Unit,
    apiKey: String,
    onApiKey: (String) -> Unit,
    speakReplies: Boolean,
    onSpeakReplies: (Boolean) -> Unit,
    updateStatus: String,
    wakeInstalled: Boolean,
    wakeInstalling: Boolean,
    wakeInstallStatus: String,
    wakeStatus: String,
    wakeEnabled: Boolean,
    wakeSensitivity: Float,
    onInstallWakeModel: () -> Unit,
    onWakeToggle: (Boolean) -> Unit,
    onWakeSensitivity: (Float) -> Unit,
    onStart: () -> Unit,
    onSubmit: () -> Unit,
    onListen: () -> Unit,
    onQuick: (String) -> Unit,
    onClearHistory: () -> Unit,
    onCheckUpdate: () -> Unit,
    onInstallUpdate: () -> Unit,
    onAccessibilitySettings: () -> Unit,
    onNotificationSettings: () -> Unit
) {
    Scaffold(
        containerColor = Background,
        bottomBar = {
            if (page != Page.Welcome) {
                Row(
                    Modifier.fillMaxWidth().background(Color(0xFF160923)).padding(10.dp),
                    horizontalArrangement = Arrangement.SpaceEvenly
                ) {
                    listOf(Page.Home, Page.Chat, Page.Voice, Page.History, Page.Settings).forEach { item ->
                        Text(
                            item.name,
                            color = if (item == page) Purple else Dim,
                            fontSize = 11.sp,
                            modifier = Modifier.clickable { onPage(item) }.padding(8.dp)
                        )
                    }
                }
            }
        }
    ) { padding ->
        Column(
            Modifier.fillMaxSize()
                .background(Brush.verticalGradient(listOf(Color(0xFF16062B), Background, Color(0xFF0B061B))))
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(18.dp)
        ) {
            if (page == Page.Welcome) {
                Spacer(Modifier.height(30.dp))
                Vortex("✦", false)
                Text("Meet Jarvis", color = TextColor, fontSize = 36.sp, fontWeight = FontWeight.Bold, modifier = Modifier.fillMaxWidth(), textAlign = TextAlign.Center)
                Text("Your voice, your device, your assistant.", color = Dim, fontSize = 16.sp, modifier = Modifier.fillMaxWidth(), textAlign = TextAlign.Center)
                Text("Device commands work offline. Connect an AI provider when you want cloud conversations.", color = Dim, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth())
                Button(onClick = onStart, modifier = Modifier.fillMaxWidth(), colors = ButtonDefaults.buttonColors(containerColor = Purple)) { Text("Get started") }
            } else {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                    Column {
                        Text("JARVIS", color = TextColor, fontSize = 27.sp, fontWeight = FontWeight.Black, letterSpacing = 3.sp)
                        Text("YOUR PERSONAL ASSISTANT", color = Dim, fontSize = 10.sp)
                    }
                    Text(if (connected) "● AI READY" else "○ LOCAL", color = if (connected) Pink else Dim, fontSize = 11.sp)
                }
                when (page) {
                    Page.Home -> {
                        Text("Good day, Azeem", color = TextColor, fontSize = 27.sp, fontWeight = FontWeight.SemiBold)
                        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                            FeatureCard("✦", "Chat with Jarvis", Modifier.weight(1f)) { onPage(Page.Chat) }
                            FeatureCard("♩", "Speak with Jarvis", Modifier.weight(1f), onListen)
                        }
                        Text("QUICK ACTIONS", color = Dim, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Action("Battery", Modifier.weight(1f)) { onQuick("battery") }
                            Action("Torch", Modifier.weight(1f)) { onQuick("flashlight on") }
                            Action("Volume", Modifier.weight(1f)) { onQuick("volume 40") }
                        }
                        Text("RECENT ACTIVITY", color = Dim, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                        Glass {
                            if (turns.isEmpty()) Text("No conversations yet.", color = Dim)
                            turns.takeLast(4).reversed().forEach {
                                Text("${if (it.role == "user") "You" else "Jarvis"}: ${it.text}", color = TextColor, fontSize = 13.sp, maxLines = 2, modifier = Modifier.padding(vertical = 5.dp))
                            }
                        }
                        if (response.isNotEmpty()) Glass { Text(response, color = TextColor) }
                    }
                    Page.Chat -> {
                        Text("Chat with Jarvis", color = TextColor, fontSize = 27.sp, fontWeight = FontWeight.SemiBold)
                        Text(if (connected) "Cloud AI connected when you send a message" else "Local commands available. Configure a provider for AI chat.", color = Dim, fontSize = 13.sp)
                        turns.takeLast(20).forEach { turn ->
                            Glass {
                                Text(if (turn.role == "user") "YOU" else "JARVIS", color = if (turn.role == "user") Pink else Purple, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                                Text(turn.text, color = TextColor, modifier = Modifier.padding(top = 6.dp))
                            }
                        }
                        if (busy) Text("Jarvis is responding…", color = Purple)
                        ChatInput(prompt, onPrompt, onSubmit, onListen, busy)
                    }
                    Page.Voice -> {
                        Spacer(Modifier.height(25.dp))
                        Vortex("♩", listening)
                        Text(if (listening) "Listening…" else "Speak with Jarvis", color = TextColor, fontSize = 27.sp, modifier = Modifier.fillMaxWidth(), textAlign = TextAlign.Center)
                        Text(if (wakeEnabled) wakeStatus else if (listening) "Speak naturally" else "Tap the microphone to speak", color = Dim, modifier = Modifier.fillMaxWidth(), textAlign = TextAlign.Center)
                        Button(onClick = onListen, modifier = Modifier.align(Alignment.CenterHorizontally), colors = ButtonDefaults.buttonColors(containerColor = Purple)) { Text("🎙  Start listening") }
                        Glass { Text(response, color = TextColor) }
                    }
                    Page.History -> {
                        Text("Conversation history", color = TextColor, fontSize = 27.sp)
                        Text("Stored encrypted on this device.", color = Dim)
                        OutlinedButton(onClick = onClearHistory) { Text("Clear history") }
                        turns.reversed().forEach { turn ->
                            Glass {
                                Text(if (turn.role == "user") "YOU" else "JARVIS", color = Pink, fontSize = 11.sp)
                                Text(turn.text, color = TextColor)
                            }
                        }
                    }
                    Page.Settings -> {
                        Text("Settings", color = TextColor, fontSize = 27.sp)
                        Glass {
                            Text("AI PROVIDER", color = Pink, fontWeight = FontWeight.Bold)
                            Text("Requests are sent to this provider only when you submit a chat message.", color = Dim, fontSize = 12.sp)
                            Field("HTTPS chat endpoint", endpoint, onEndpoint)
                            Field("Model", model, onModel)
                            Field("API key (stored encrypted)", apiKey, onApiKey, secret = true)
                            Text(if (connected) "● Key configured" else "○ No provider key configured", color = if (connected) Pink else Dim)
                        }
                        Glass {
                            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                Text("Speak responses", color = TextColor)
                                Switch(checked = speakReplies, onCheckedChange = onSpeakReplies)
                            }
                            Text("ACCESS", color = Pink, fontWeight = FontWeight.Bold)
                            OutlinedButton(onClick = onAccessibilitySettings) { Text("Accessibility controls") }
                            OutlinedButton(onClick = onNotificationSettings) { Text("Notification access") }
                            Text("Enable these Android permissions only if you want voice control of other apps or notification summaries.", color = Dim, fontSize = 12.sp)
                            Text("HEY JARVIS • OFFLINE", color = Pink, fontWeight = FontWeight.Bold)
                            Text(wakeInstallStatus, color = Dim, fontSize = 12.sp)
                            if (!wakeInstalled) {
                                Button(onClick = onInstallWakeModel, enabled = !wakeInstalling) {
                                    Text(if (wakeInstalling) "Installing…" else "Install offline model")
                                }
                            }
                            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                Text("Enable wake phrase", color = TextColor)
                                Switch(checked = wakeEnabled, onCheckedChange = onWakeToggle, enabled = wakeInstalled && !wakeInstalling)
                            }
                            Text(wakeStatus, color = Dim, fontSize = 12.sp)
                            Text("Sensitivity ${(wakeSensitivity * 100).toInt()}%", color = TextColor, fontSize = 12.sp)
                            Slider(value = wakeSensitivity, onValueChange = onWakeSensitivity, valueRange = 0.35f..0.9f)
                            Text("Android shows a persistent microphone notification while listening. Audio stays on this device; recognized text goes to your AI provider only for requests that need it.", color = Dim, fontSize = 12.sp)
                            Text("Microphone starts only when you tap Speak. Speech recognition may use your device's configured service.", color = Dim, fontSize = 12.sp)
                        }
                        Glass {
                            Text("APP UPDATE", color = Pink, fontWeight = FontWeight.Bold)
                            Text(updateStatus, color = TextColor, fontSize = 13.sp)
                            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                OutlinedButton(onClick = onCheckUpdate) { Text("Check") }
                                if (updateStatus.contains("available")) Button(onClick = onInstallUpdate) { Text("Install") }
                            }
                        }
                    }
                    Page.Welcome -> Unit
                }
            }
        }
    }
}

@Composable
private fun Field(label: String, value: String, changed: (String) -> Unit, secret: Boolean = false) {
    OutlinedTextField(
        value = value,
        onValueChange = changed,
        label = { Text(label) },
        modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
        singleLine = true,
        visualTransformation = if (secret) PasswordVisualTransformation() else VisualTransformation.None,
        colors = OutlinedTextFieldDefaults.colors(focusedBorderColor = Purple, unfocusedBorderColor = Color(0xFF5A3A76), focusedTextColor = TextColor, unfocusedTextColor = TextColor)
    )
}

@Composable
private fun ChatInput(prompt: String, onPrompt: (String) -> Unit, onSubmit: () -> Unit, onListen: () -> Unit, busy: Boolean) {
    Glass {
        Field("Ask Jarvis or enter a device command", prompt, onPrompt)
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.padding(top = 10.dp)) {
            OutlinedButton(onClick = onListen, modifier = Modifier.weight(1f)) { Text("Speak") }
            Button(onClick = onSubmit, enabled = !busy, modifier = Modifier.weight(1f), colors = ButtonDefaults.buttonColors(containerColor = Purple)) { Text("Send") }
        }
    }
}

@Composable
private fun FeatureCard(symbol: String, title: String, modifier: Modifier, action: () -> Unit) {
    Card(onClick = action, modifier = modifier.height(180.dp), shape = RoundedCornerShape(28.dp), border = BorderStroke(1.dp, Color(0xFF6E368A)), colors = CardDefaults.cardColors(containerColor = Panel)) {
        Column(Modifier.fillMaxSize().padding(14.dp), verticalArrangement = Arrangement.SpaceBetween, horizontalAlignment = Alignment.CenterHorizontally) {
            Box(Modifier.size(90.dp).clip(RoundedCornerShape(22.dp)).background(Brush.radialGradient(listOf(Color(0xFF6832A4), Color(0xFF210F38)))), contentAlignment = Alignment.Center) {
                Text(symbol, color = TextColor, fontSize = 36.sp)
            }
            Text(title, color = TextColor, textAlign = TextAlign.Center, fontSize = 14.sp)
        }
    }
}

@Composable
private fun Action(text: String, modifier: Modifier, action: () -> Unit) {
    OutlinedButton(onClick = action, modifier = modifier, border = BorderStroke(1.dp, Purple), contentPadding = androidx.compose.foundation.layout.PaddingValues(4.dp)) { Text(text, fontSize = 11.sp) }
}

@Composable
private fun Glass(content: @Composable androidx.compose.foundation.layout.ColumnScope.() -> Unit) {
    Card(modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(22.dp), border = BorderStroke(1.dp, Color(0xFF593474)), colors = CardDefaults.cardColors(containerColor = Panel)) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp), content = content)
    }
}

@Composable
private fun Vortex(symbol: String, listening: Boolean) {
    val transition = rememberInfiniteTransition(label = "vortex")
    val scale by transition.animateFloat(0.94f, 1.06f, infiniteRepeatable(tween(if (listening) 600 else 1800), RepeatMode.Reverse), label = "pulse")
    Box(Modifier.fillMaxWidth().height(290.dp), contentAlignment = Alignment.Center) {
        Box(Modifier.size(270.dp).graphicsLayer(scaleX = scale, scaleY = scale).clip(CircleShape).background(Brush.radialGradient(listOf(Color(0x996F24C7), Color(0x5534137A), Color.Transparent))))
        Box(Modifier.size(220.dp).clip(CircleShape).background(Brush.sweepGradient(listOf(Purple, Pink, Color(0xFF3422BB), Purple))).padding(3.dp).clip(CircleShape).background(Background), contentAlignment = Alignment.Center) {
            Box(Modifier.size(178.dp).clip(CircleShape).background(Brush.radialGradient(listOf(Color(0xFF61209E), Color(0xFF1C0739), Background))), contentAlignment = Alignment.Center) {
                Text(symbol, color = TextColor, fontSize = 88.sp, fontWeight = FontWeight.Bold)
            }
        }
    }
}
