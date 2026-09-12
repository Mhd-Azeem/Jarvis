package com.azeem.jarvis.core

import android.content.Context
import android.content.Intent
import android.hardware.camera2.CameraCharacteristics
import android.hardware.camera2.CameraManager
import android.media.AudioManager
import android.os.BatteryManager

class JarvisCommandProcessor(private val context: Context) {

    fun execute(rawCommand: String): String {
        val command = rawCommand.trim()
        val lower = command.lowercase()
        if (command.isBlank()) return "I didn't hear a command."

        return when {
            lower.startsWith("open ") -> openApp(command.substringAfter(' ').trim())
            lower.contains("flashlight on") || lower.contains("torch on") -> setTorch(true)
            lower.contains("flashlight off") || lower.contains("torch off") -> setTorch(false)
            lower.startsWith("volume ") || lower.contains("volume to ") -> setVolume(command)
            lower.contains("battery") -> batteryStatus()
            else -> "That command is not available in this compatibility build. Try open YouTube, flashlight on, volume 40, or battery."
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
