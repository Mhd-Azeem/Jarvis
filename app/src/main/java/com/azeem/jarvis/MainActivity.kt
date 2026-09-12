package com.azeem.jarvis

import android.content.Intent
import android.os.Bundle
import android.speech.RecognizerIntent
import android.speech.tts.TextToSpeech
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
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
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
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
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.lifecycleScope
import com.azeem.jarvis.core.JarvisCommandProcessor
import com.azeem.jarvis.update.UpdateManager
import kotlinx.coroutines.launch
import java.util.Locale

private val JarvisCyan = Color(0xFF53E9FF)
private val JarvisBlue = Color(0xFF4A7DFF)
private val JarvisPurple = Color(0xFF8D63FF)
private val JarvisBg = Color(0xFF060A12)
private val JarvisPanel = Color(0xC9141C2A)
private val JarvisPanelSoft = Color(0x99172232)
private val JarvisText = Color(0xFFF2F7FF)
private val JarvisMuted = Color(0xFF90A0B8)

class MainActivity : ComponentActivity(), TextToSpeech.OnInitListener {

    private lateinit var commandProcessor: JarvisCommandProcessor
    private lateinit var updateManager: UpdateManager
    private var tts: TextToSpeech? = null
    private var updateStatus by mutableStateOf("Checking GitHub for updates…")
    private var pendingUpdate by mutableStateOf<UpdateManager.AvailableUpdate?>(null)
    private var updateBusy by mutableStateOf(false)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        commandProcessor = JarvisCommandProcessor(applicationContext)
        updateManager = UpdateManager(this)
        tts = TextToSpeech(this, this)

        setContent {
            var command by mutableStateOf("")
            var response by mutableStateOf("Systems online. Ready for your command.")
            var isListening by mutableStateOf(false)

            val speechLauncher = rememberLauncherForActivityResult(
                ActivityResultContracts.StartActivityForResult()
            ) { result ->
                isListening = false
                val heard = result.data
                    ?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)
                    ?.firstOrNull()
                if (!heard.isNullOrBlank()) {
                    command = heard
                    response = runCommand(heard)
                } else {
                    response = "I didn't catch that. Try speaking again."
                }
            }

            val colorScheme = darkColorScheme(
                primary = JarvisCyan,
                secondary = JarvisBlue,
                tertiary = JarvisPurple,
                background = JarvisBg,
                surface = JarvisPanel,
                onPrimary = Color(0xFF001014),
                onBackground = JarvisText,
                onSurface = JarvisText
            )

            MaterialTheme(colorScheme = colorScheme) {
                Surface(modifier = Modifier.fillMaxSize(), color = JarvisBg) {
                    JarvisDashboard(
                        command = command,
                        onCommandChange = { command = it },
                        response = response,
                        isListening = isListening,
                        updateStatus = updateStatus,
                        updateAvailable = pendingUpdate != null,
                        updateBusy = updateBusy,
                        onRun = { response = runCommand(command) },
                        onListen = {
                            isListening = true
                            val intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
                                putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
                                putExtra(RecognizerIntent.EXTRA_LANGUAGE, Locale.getDefault())
                                putExtra(RecognizerIntent.EXTRA_PROMPT, "Speak to Jarvis")
                            }
                            speechLauncher.launch(intent)
                        },
                        onQuickCommand = { quick ->
                            command = quick
                            response = runCommand(quick)
                        },
                        onCheckUpdate = ::checkForUpdates,
                        onInstallUpdate = ::installPendingUpdate
                    )
                }
            }
        }

        checkForUpdates()
    }

    override fun onInit(status: Int) {
        if (status == TextToSpeech.SUCCESS) {
            tts?.language = Locale.getDefault()
            tts?.setSpeechRate(0.95f)
        }
    }

    override fun onDestroy() {
        tts?.stop()
        tts?.shutdown()
        super.onDestroy()
    }

    private fun runCommand(command: String): String {
        val result = commandProcessor.execute(command)
        tts?.speak(result, TextToSpeech.QUEUE_FLUSH, null, "jarvis-response")
        return result
    }

    private fun checkForUpdates() {
        if (updateBusy) return
        updateBusy = true
        updateStatus = "Checking GitHub for updates…"
        lifecycleScope.launch {
            when (val result = updateManager.checkForUpdate()) {
                is UpdateManager.CheckResult.Available -> {
                    pendingUpdate = result.update
                    updateStatus = "Build #${result.update.buildNumber} is ready to install."
                }
                is UpdateManager.CheckResult.Current -> {
                    pendingUpdate = null
                    updateStatus = "You're running the latest Jarvis build."
                }
                is UpdateManager.CheckResult.Error -> updateStatus = result.message
            }
            updateBusy = false
        }
    }

    private fun installPendingUpdate() {
        val update = pendingUpdate ?: run {
            checkForUpdates()
            return
        }
        if (updateBusy) return
        updateBusy = true
        updateStatus = "Downloading Build #${update.buildNumber}…"
        lifecycleScope.launch {
            updateStatus = updateManager.downloadAndInstall(update)
            updateBusy = false
        }
    }
}

@Composable
private fun JarvisDashboard(
    command: String,
    onCommandChange: (String) -> Unit,
    response: String,
    isListening: Boolean,
    updateStatus: String,
    updateAvailable: Boolean,
    updateBusy: Boolean,
    onRun: () -> Unit,
    onListen: () -> Unit,
    onQuickCommand: (String) -> Unit,
    onCheckUpdate: () -> Unit,
    onInstallUpdate: () -> Unit
) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(
                Brush.verticalGradient(
                    listOf(Color(0xFF050811), Color(0xFF08111E), Color(0xFF050811))
                )
            )
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 18.dp, vertical = 20.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            HeaderRow()
            JarvisCore(isListening = isListening)

            Text(
                text = if (isListening) "LISTENING…" else "HOW CAN I HELP?",
                modifier = Modifier.fillMaxWidth(),
                color = if (isListening) JarvisCyan else JarvisText,
                fontWeight = FontWeight.Bold,
                fontSize = 22.sp,
                textAlign = TextAlign.Center,
                letterSpacing = 1.4.sp
            )
            Text(
                text = if (isListening) "Speak naturally. Jarvis is listening." else "Type a command or use a quick action below.",
                modifier = Modifier.fillMaxWidth(),
                color = JarvisMuted,
                fontSize = 13.sp,
                textAlign = TextAlign.Center
            )

            GlassCard {
                OutlinedTextField(
                    value = command,
                    onValueChange = onCommandChange,
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text("Command") },
                    placeholder = { Text("Open YouTube, battery, volume 40…") },
                    minLines = 2,
                    maxLines = 4,
                    shape = RoundedCornerShape(18.dp),
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedBorderColor = JarvisCyan,
                        unfocusedBorderColor = Color(0xFF2C3B50),
                        focusedLabelColor = JarvisCyan,
                        cursorColor = JarvisCyan,
                        focusedTextColor = JarvisText,
                        unfocusedTextColor = JarvisText,
                        focusedContainerColor = Color(0x66101825),
                        unfocusedContainerColor = Color(0x55101825)
                    )
                )

                Spacer(Modifier.height(12.dp))
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    OutlinedButton(
                        onClick = onListen,
                        modifier = Modifier.weight(1f),
                        shape = RoundedCornerShape(16.dp),
                        border = BorderStroke(1.dp, if (isListening) JarvisCyan else Color(0xFF36516C)),
                        colors = ButtonDefaults.outlinedButtonColors(contentColor = JarvisCyan)
                    ) {
                        Text(if (isListening) "● Listening" else "◉ Speak", fontWeight = FontWeight.SemiBold)
                    }
                    Button(
                        onClick = onRun,
                        modifier = Modifier.weight(1f),
                        shape = RoundedCornerShape(16.dp),
                        colors = ButtonDefaults.buttonColors(containerColor = JarvisCyan, contentColor = Color(0xFF001014))
                    ) {
                        Text("Execute  ›", fontWeight = FontWeight.Bold)
                    }
                }
            }

            Text("QUICK ACTIONS", color = JarvisMuted, fontSize = 11.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.4.sp)
            QuickActions(onQuickCommand)

            GlassCard {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(
                        modifier = Modifier
                            .size(10.dp)
                            .clip(CircleShape)
                            .background(JarvisCyan)
                    )
                    Spacer(Modifier.size(10.dp))
                    Text("JARVIS RESPONSE", color = JarvisCyan, fontSize = 11.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.1.sp)
                }
                Spacer(Modifier.height(10.dp))
                Text(response, color = JarvisText, fontSize = 16.sp, lineHeight = 23.sp)
            }

            GlassCard {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text("SYSTEM UPDATE", color = JarvisMuted, fontSize = 11.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.1.sp)
                        Spacer(Modifier.height(5.dp))
                        Text(updateStatus, color = JarvisText, fontSize = 14.sp)
                    }
                    Spacer(Modifier.size(10.dp))
                    Text("#${BuildConfig.VERSION_CODE}", color = JarvisCyan, fontWeight = FontWeight.Bold)
                }
                AnimatedVisibility(updateBusy) {
                    Column {
                        Spacer(Modifier.height(10.dp))
                        LinearProgressIndicator(
                            modifier = Modifier.fillMaxWidth(),
                            color = JarvisCyan,
                            trackColor = Color(0xFF1B2A3C)
                        )
                    }
                }
                Spacer(Modifier.height(10.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    OutlinedButton(
                        onClick = onCheckUpdate,
                        enabled = !updateBusy,
                        shape = RoundedCornerShape(14.dp),
                        colors = ButtonDefaults.outlinedButtonColors(contentColor = JarvisCyan)
                    ) { Text("Check update") }
                    if (updateAvailable) {
                        Button(
                            onClick = onInstallUpdate,
                            enabled = !updateBusy,
                            shape = RoundedCornerShape(14.dp),
                            colors = ButtonDefaults.buttonColors(containerColor = JarvisCyan, contentColor = Color(0xFF001014))
                        ) { Text("Install", fontWeight = FontWeight.Bold) }
                    }
                }
            }

            Text(
                text = "Compatibility mode • Standard Android controls • Auto-update enabled",
                modifier = Modifier.fillMaxWidth().padding(bottom = 6.dp),
                color = Color(0xFF617086),
                fontSize = 11.sp,
                textAlign = TextAlign.Center
            )
        }
    }
}

@Composable
private fun HeaderRow() {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column {
            Text("JARVIS", color = JarvisText, fontSize = 28.sp, fontWeight = FontWeight.Black, letterSpacing = 3.sp)
            Text("PERSONAL ASSISTANT", color = JarvisMuted, fontSize = 10.sp, letterSpacing = 1.7.sp)
        }
        Row(
            verticalAlignment = Alignment.CenterVertically,
            modifier = Modifier
                .clip(RoundedCornerShape(50))
                .background(Color(0x3317D7A0))
                .border(1.dp, Color(0x5529E7B0), RoundedCornerShape(50))
                .padding(horizontal = 11.dp, vertical = 7.dp)
        ) {
            Box(Modifier.size(7.dp).clip(CircleShape).background(Color(0xFF35F0B1)))
            Spacer(Modifier.size(7.dp))
            Text("ONLINE", color = Color(0xFF67F4C7), fontSize = 10.sp, fontWeight = FontWeight.Bold)
        }
    }
}

@Composable
private fun JarvisCore(isListening: Boolean) {
    val transition = rememberInfiniteTransition(label = "jarvis-core")
    val pulse by transition.animateFloat(
        initialValue = 0.92f,
        targetValue = 1.08f,
        animationSpec = infiniteRepeatable(tween(if (isListening) 650 else 1600), RepeatMode.Reverse),
        label = "pulse"
    )
    val glowAlpha by transition.animateFloat(
        initialValue = 0.35f,
        targetValue = 0.85f,
        animationSpec = infiniteRepeatable(tween(if (isListening) 500 else 1400), RepeatMode.Reverse),
        label = "glow"
    )

    Box(
        modifier = Modifier.fillMaxWidth().height(190.dp),
        contentAlignment = Alignment.Center
    ) {
        Box(
            modifier = Modifier
                .size(164.dp)
                .graphicsLayer(scaleX = pulse, scaleY = pulse, alpha = glowAlpha)
                .clip(CircleShape)
                .background(
                    Brush.radialGradient(
                        listOf(Color(0x8853E9FF), Color(0x224A7DFF), Color.Transparent)
                    )
                )
        )
        Box(
            modifier = Modifier
                .size(132.dp)
                .clip(CircleShape)
                .border(2.dp, Color(0xAA53E9FF), CircleShape)
                .background(
                    Brush.radialGradient(
                        listOf(Color(0xFF0A2D3B), Color(0xFF0A1424), Color(0xFF070A12))
                    )
                ),
            contentAlignment = Alignment.Center
        ) {
            Box(
                modifier = Modifier
                    .size(84.dp)
                    .clip(CircleShape)
                    .border(1.dp, Color(0xFF6AF1FF), CircleShape)
                    .background(
                        Brush.radialGradient(
                            listOf(Color(0xFF7AF4FF), Color(0xFF247F9D), Color(0xFF092535))
                        )
                    ),
                contentAlignment = Alignment.Center
            ) {
                Text(if (isListening) "●" else "J", color = Color.White, fontSize = 28.sp, fontWeight = FontWeight.Black)
            }
        }
    }
}

@Composable
private fun QuickActions(onQuickCommand: (String) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(9.dp)) {
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(9.dp)) {
            QuickAction("◈", "YouTube", "open YouTube", Modifier.weight(1f), onQuickCommand)
            QuickAction("⌁", "Battery", "battery", Modifier.weight(1f), onQuickCommand)
            QuickAction("✦", "Flashlight", "flashlight on", Modifier.weight(1f), onQuickCommand)
        }
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(9.dp)) {
            QuickAction("◐", "Volume 40", "volume 40", Modifier.weight(1f), onQuickCommand)
            QuickAction("⚙", "Settings", "open settings", Modifier.weight(1f), onQuickCommand)
            QuickAction("⌁", "Bluetooth", "Bluetooth settings", Modifier.weight(1f), onQuickCommand)
        }
    }
}

@Composable
private fun QuickAction(
    symbol: String,
    label: String,
    command: String,
    modifier: Modifier,
    onQuickCommand: (String) -> Unit
) {
    OutlinedButton(
        onClick = { onQuickCommand(command) },
        modifier = modifier.height(68.dp),
        shape = RoundedCornerShape(18.dp),
        border = BorderStroke(1.dp, Color(0xFF253A50)),
        colors = ButtonDefaults.outlinedButtonColors(
            containerColor = Color(0x66111B29),
            contentColor = JarvisText
        ),
        contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 6.dp, vertical = 7.dp)
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text(symbol, color = JarvisCyan, fontSize = 18.sp, fontWeight = FontWeight.Bold)
            Text(label, color = JarvisText, fontSize = 10.sp, maxLines = 1)
        }
    }
}

@Composable
private fun GlassCard(content: @Composable ColumnScope.() -> Unit) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(22.dp),
        border = BorderStroke(1.dp, Color(0xFF1E344A)),
        colors = CardDefaults.cardColors(containerColor = JarvisPanelSoft)
    ) {
        Column(modifier = Modifier.padding(16.dp), content = content)
    }
}
