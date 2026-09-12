package com.azeem.jarvis

import android.content.Intent
import android.os.Bundle
import android.provider.Settings
import android.speech.RecognizerIntent
import android.speech.tts.TextToSpeech
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.core.app.NotificationManagerCompat
import com.azeem.jarvis.core.JarvisCommandProcessor
import com.azeem.jarvis.service.JarvisAccessibilityService
import java.util.Locale

class MainActivity : ComponentActivity(), TextToSpeech.OnInitListener {

    private lateinit var commandProcessor: JarvisCommandProcessor
    private var tts: TextToSpeech? = null
    private var accessibilityEnabled by mutableStateOf(false)
    private var notificationAccessEnabled by mutableStateOf(false)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        commandProcessor = JarvisCommandProcessor(applicationContext)
        tts = TextToSpeech(this, this)
        refreshServiceStates()

        setContent {
            MaterialTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    var command by mutableStateOf("")
                    var response by mutableStateOf("Ready. Say or type a command.")

                    val speechLauncher = rememberLauncherForActivityResult(
                        ActivityResultContracts.StartActivityForResult()
                    ) { result ->
                        val heard = result.data
                            ?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)
                            ?.firstOrNull()
                        if (!heard.isNullOrBlank()) {
                            command = heard
                            response = runCommand(heard)
                        }
                    }

                    JarvisDashboard(
                        command = command,
                        onCommandChange = { command = it },
                        response = response,
                        accessibilityEnabled = accessibilityEnabled,
                        notificationAccessEnabled = notificationAccessEnabled,
                        onRun = { response = runCommand(command) },
                        onListen = {
                            val intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
                                putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
                                putExtra(RecognizerIntent.EXTRA_LANGUAGE, Locale.getDefault())
                                putExtra(RecognizerIntent.EXTRA_PROMPT, "Speak to Jarvis")
                            }
                            speechLauncher.launch(intent)
                        },
                        onAccessibilitySettings = {
                            startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
                        },
                        onNotificationSettings = {
                            startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS))
                        }
                    )
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        refreshServiceStates()
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
        refreshServiceStates()
        return result
    }

    private fun refreshServiceStates() {
        accessibilityEnabled = JarvisAccessibilityService.instance != null
        notificationAccessEnabled = NotificationManagerCompat
            .getEnabledListenerPackages(this)
            .contains(packageName)
    }
}

@androidx.compose.runtime.Composable
private fun JarvisDashboard(
    command: String,
    onCommandChange: (String) -> Unit,
    response: String,
    accessibilityEnabled: Boolean,
    notificationAccessEnabled: Boolean,
    onRun: () -> Unit,
    onListen: () -> Unit,
    onAccessibilitySettings: () -> Unit,
    onNotificationSettings: () -> Unit
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        Text("JARVIS", style = MaterialTheme.typography.headlineLarge)
        Text("Personal Android control assistant", style = MaterialTheme.typography.bodyLarge)

        StatusCard("Accessibility control", accessibilityEnabled, onAccessibilitySettings)
        StatusCard("Notification access", notificationAccessEnabled, onNotificationSettings)

        OutlinedTextField(
            value = command,
            onValueChange = onCommandChange,
            modifier = Modifier.fillMaxWidth(),
            label = { Text("Command") },
            placeholder = { Text("e.g. open YouTube, scroll down, volume 40") },
            minLines = 2
        )

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Button(onClick = onListen, modifier = Modifier.weight(1f)) {
                Text("Speak")
            }
            Button(onClick = onRun, modifier = Modifier.weight(1f)) {
                Text("Run")
            }
        }

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(16.dp)) {
                Text("Jarvis", style = MaterialTheme.typography.titleMedium)
                Spacer(Modifier.height(8.dp))
                Text(response)
            }
        }

        Text("Commands available now", style = MaterialTheme.typography.titleMedium)
        Text(
            "• open YouTube\n" +
                "• flashlight on / off\n" +
                "• volume 40\n" +
                "• battery\n" +
                "• back / home / recents\n" +
                "• notifications / quick settings\n" +
                "• scroll down / scroll up\n" +
                "• tap Search\n" +
                "• type hello\n" +
                "• read notifications"
        )
    }
}

@androidx.compose.runtime.Composable
private fun StatusCard(
    title: String,
    enabled: Boolean,
    onSettings: () -> Unit
) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Column {
                Text(title, style = MaterialTheme.typography.titleMedium)
                Text(if (enabled) "Enabled" else "Required")
            }
            OutlinedButton(onClick = onSettings) {
                Text(if (enabled) "Settings" else "Enable")
            }
        }
    }
}
