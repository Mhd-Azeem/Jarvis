package com.azeem.jarvis.core

import android.content.Context
import android.content.Intent
import android.hardware.camera2.CameraCharacteristics
import android.hardware.camera2.CameraManager
import android.media.AudioManager
import android.view.KeyEvent
import android.provider.Settings
import android.provider.AlarmClock
import java.text.DateFormat
import java.util.Date
import android.os.BatteryManager

class JarvisCommandProcessor(private val context: Context) {

    companion object {
    fun supports(rawCommand: String): Boolean {
        val value = rawCommand.trim().lowercase()
        return value.startsWith("open ") ||
            value.contains("flashlight on") || value.contains("flashlight off") ||
            value.contains("torch on") || value.contains("torch off") ||
            value.startsWith("volume ") || value.contains("volume to ") ||
            value.contains("battery") || value == "time" || value == "date" ||
            value == "open settings" || value == "wifi settings" || value == "bluetooth settings" ||
            value == "play pause" || value == "next track" || value == "previous track" ||
            value == "read notifications" || value in setOf("back", "home", "recents", "notifications", "quick settings", "scroll up", "scroll down") ||
            value.startsWith("tap ") || value.startsWith("type ") || value.startsWith("set alarm ")
    }

    }

    fun execute(rawCommand: String): String {
        val command = rawCommand.trim()
        val lower = command.lowercase()
        if (command.isBlank()) return "I didn't hear a command."

        return when {
            lower == "time" -> DateFormat.getTimeInstance(DateFormat.SHORT).format(Date())
            lower == "date" -> DateFormat.getDateInstance(DateFormat.FULL).format(Date())
            lower == "read notifications" -> JarvisNotificationListener.active?.summary()
                ?: "Enable notification access for Jarvis in Android Settings first."
            lower in setOf("back", "home", "recents", "notifications", "quick settings", "scroll up", "scroll down") ||
                lower.startsWith("tap ") || lower.startsWith("type ") ->
                JarvisAccessibilityService.active?.execute(command)
                    ?: "Enable the Jarvis accessibility service in Android Settings first."
            lower == "open settings" -> openSettings(Settings.ACTION_SETTINGS)
            lower == "wifi settings" -> openSettings(Settings.ACTION_WIFI_SETTINGS)
            lower == "bluetooth settings" -> openSettings(Settings.ACTION_BLUETOOTH_SETTINGS)
            lower == "play pause" -> mediaKey(KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE)
            lower == "next track" -> mediaKey(KeyEvent.KEYCODE_MEDIA_NEXT)
            lower == "previous track" -> mediaKey(KeyEvent.KEYCODE_MEDIA_PREVIOUS)
            lower.startsWith("set alarm ") -> setAlarm(command.substringAfter("set alarm ").trim())
            lower.startsWith("open ") -> openApp(command.substringAfter(' ').trim())
            lower.contains("flashlight on") || lower.contains("torch on") -> setTorch(true)
            lower.contains("flashlight off") || lower.contains("torch off") -> setTorch(false)
            lower.startsWith("volume ") || lower.contains("volume to ") -> setVolume(command)
            lower.contains("battery") -> batteryStatus()
            else -> "That command is not available in this compatibility build. Try open YouTube, flashlight on, volume 40, or battery."
        }
    }

    private fun openSettings(action: String): String = try {
        context.startActivity(Intent(action).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        "Opening Android settings."
    } catch (_: Exception) {
        "Android could not open those settings."
    }

    private fun mediaKey(code: Int): String = try {
        val audio = context.getSystemService(AudioManager::class.java)
        audio.dispatchMediaKeyEvent(KeyEvent(KeyEvent.ACTION_DOWN, code))
        audio.dispatchMediaKeyEvent(KeyEvent(KeyEvent.ACTION_UP, code))
        "Media command sent."
    } catch (_: Exception) {
        "No media app accepted that command."
    }

    private fun setAlarm(time: String): String {
        val match = Regex("^(\\d{1,2}):(\\d{2})$").find(time)
            ?: return "Say set alarm followed by a time such as 07:30."
        val hour = match.groupValues[1].toInt()
        val minute = match.groupValues[2].toInt()
        if (hour !in 0..23 || minute !in 0..59) return "Use a 24-hour time from 00:00 to 23:59."
        return try {
            context.startActivity(Intent(AlarmClock.ACTION_SET_ALARM).apply {
                putExtra(AlarmClock.EXTRA_HOUR, hour)
                putExtra(AlarmClock.EXTRA_MINUTES, minute)
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            })
            "Opening the clock to set your alarm."
        } catch (_: Exception) {
            "No clock app is available to set the alarm."
        }
    }
    private fun openApp(name: String): String {
        val pm = context.packageManager
        val queryIntent = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER)
        val matches = pm.queryIntentActivities(queryIntent, 0)
        val match = matches.firstOrNull {
            it.loadLabel(pm).toString().equals(name, ignoreCase = true)
        } ?: matches.firstOrNull {
            it.loadLabel(pm).toString().contains(name, ignoreCase = true)
        } ?: return "I couldn't find $name on this phone."

        val launch = pm.getLaunchIntentForPackage(match.activityInfo.packageName)
            ?: return "I found $name, but Android didn't provide a launch action."
        launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        context.startActivity(launch)
        return "Opening ${match.loadLabel(pm)}."
    }

    private fun setTorch(enabled: Boolean): String {
        return try {
            val cameraManager = context.getSystemService(CameraManager::class.java)
            val cameraId = cameraManager.cameraIdList.firstOrNull { id ->
                cameraManager.getCameraCharacteristics(id)
                    .get(CameraCharacteristics.FLASH_INFO_AVAILABLE) == true
            } ?: return "This phone doesn't report an available flashlight."
            cameraManager.setTorchMode(cameraId, enabled)
            if (enabled) "Flashlight on." else "Flashlight off."
        } catch (_: Exception) {
            "I couldn't change the flashlight."
        }
    }

    private fun setVolume(command: String): String {
        val percent = Regex("(\\d{1,3})").find(command)?.value?.toIntOrNull()
            ?: return "Tell me a volume from 0 to 100."
        val safePercent = percent.coerceIn(0, 100)
        val audio = context.getSystemService(AudioManager::class.java)
        val max = audio.getStreamMaxVolume(AudioManager.STREAM_MUSIC)
        val target = ((safePercent / 100f) * max).toInt()
        audio.setStreamVolume(AudioManager.STREAM_MUSIC, target, AudioManager.FLAG_SHOW_UI)
        return "Media volume set to $safePercent percent."
    }

    private fun batteryStatus(): String {
        val battery = context.getSystemService(BatteryManager::class.java)
        val level = battery.getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY)
        return if (level in 0..100) "Battery is at $level percent." else "I couldn't read the battery level."
    }
}
