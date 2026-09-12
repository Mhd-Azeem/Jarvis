package com.azeem.jarvis

import android.content.Intent
import android.os.Bundle
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
import androidx.lifecycle.lifecycleScope
import com.azeem.jarvis.core.JarvisCommandProcessor
import com.azeem.jarvis.update.UpdateManager
import kotlinx.coroutines.launch
import java.util.Locale

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
                        updateStatus = updateStatus,
                        updateAvailable = pendingUpdate != null,
                        updateBusy = updateBusy,
                        onRun = { response = runCommand(command) },
                        onListen = {
                            val intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
                                putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
                                putExtra(RecognizerIntent.EXTRA_LANGUAGE, Locale.getDefault())
                                putExtra(RecognizerIntent.EXTRA_PROMPT, "Speak to Jarvis")
                            }
                            speechLauncher.launch(intent)
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
                    updateStatus = "Jarvis Build #${result.update.buildNumber} is available."
                }
                is UpdateManager.CheckResult.Current -> {
                    pendingUpdate = null
                    updateStatus = "Jarvis is up to date (Build #${result.currentBuild})."
                }
                is UpdateManager.CheckResult.Error -> {
                    updateStatus = result.message
                }
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
        updateStatus = "Downloading Jarvis Build #${update.buildNumber}…"
        lifecycleScope.launch {
            updateStatus = updateManager.downloadAndInstall(update)
            updateBusy = false
        }
    }
}

@androidx.compose.runtime.Composable
private fun JarvisDashboard(
    command: String,
    onCommandChange: (String) -> Unit,
    response: String,
    updateStatus: String,
    updateAvailable: Boolean,
    updateBusy: Boolean,
    onRun: () -> Unit,
    onListen: () -> Unit,
    onCheckUpdate: () -> Unit,
    onInstallUpdate: () -> Unit
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        Text("JARVIS", style = MaterialTheme.typography.headlineLarge)
        Text("Personal Android assistant • Build #${BuildConfig.VERSION_CODE}", style = MaterialTheme.typography.bodyLarge)

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(16.dp)) {
                Text("Automatic updates", style = MaterialTheme.typography.titleMedium)
                Spacer(Modifier.height(6.dp))
                Text(updateStatus)
                Spacer(Modifier.height(10.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    OutlinedButton(onClick = onCheckUpdate, enabled = !updateBusy) {
                        Text(if (updateBusy) "Please wait…" else "Check")
                    }
                    if (updateAvailable) {
                        Button(onClick = onInstallUpdate, enabled = !updateBusy) {
                            Text("Download & Update")
                        }
                    }
                }
                Spacer(Modifier.height(8.dp))
                Text("Android will still ask you to confirm the final system Update installation.")
            }
        }

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(16.dp)) {
                Text("Compatibility mode", style = MaterialTheme.typography.titleMedium)
                Spacer(Modifier.height(6.dp))
                Text("Advanced screen automation and notification-reading services are not included in this build. Normal Android assistant functions remain available.")
            }
        }

        OutlinedTextField(
            value = command,
            onValueChange = onCommandChange,
            modifier = Modifier.fillMaxWidth(),
            label = { Text("Command") },
            placeholder = { Text("e.g. open YouTube, flashlight on, volume 40") },
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
            "• open YouTube / WhatsApp / another installed app\n" +
                "• flashlight on / off\n" +
                "• volume 40\n" +
                "• battery\n" +
                "• open settings\n" +
                "• Wi-Fi settings\n" +
                "• Bluetooth settings\n" +
                "• display settings\n" +
                "• sound settings"
        )
    }
}
