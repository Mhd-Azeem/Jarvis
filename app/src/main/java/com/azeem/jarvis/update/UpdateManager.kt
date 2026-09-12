package com.azeem.jarvis.update

import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.content.FileProvider
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

class UpdateManager(private val activity: Activity) {

    data class AvailableUpdate(
        val buildNumber: Int,
        val tag: String,
        val downloadUrl: String
    )

    sealed interface CheckResult {
        data class Available(val update: AvailableUpdate) : CheckResult
        data class Current(val currentBuild: Int) : CheckResult
        data class Error(val message: String) : CheckResult
    }

    suspend fun checkForUpdate(): CheckResult = withContext(Dispatchers.IO) {
        try {
            val connection = (URL(LATEST_RELEASE_API).openConnection() as HttpURLConnection).apply {
                requestMethod = "GET"
                connectTimeout = 10_000
                readTimeout = 10_000
                setRequestProperty("Accept", "application/vnd.github+json")
                setRequestProperty("User-Agent", "Jarvis-Android-Updater")
                setRequestProperty("X-GitHub-Api-Version", "2022-11-28")
            }

            val responseCode = connection.responseCode
            if (responseCode !in 200..299) {
                connection.disconnect()
                return@withContext CheckResult.Error("Update check failed (HTTP $responseCode).")
            }

            val body = connection.inputStream.bufferedReader().use { it.readText() }
            connection.disconnect()
            val json = JSONObject(body)
            val tag = json.optString("tag_name")
            val latestBuild = Regex("build-(\\d+)", RegexOption.IGNORE_CASE)
                .find(tag)
                ?.groupValues
                ?.getOrNull(1)
                ?.toIntOrNull()
                ?: return@withContext CheckResult.Error("Latest release has an unknown version tag.")

            val currentBuild = currentBuildNumber()
            if (latestBuild <= currentBuild) {
                return@withContext CheckResult.Current(currentBuild)
            }

            val assets = json.optJSONArray("assets")
            var apkUrl: String? = null
            if (assets != null) {
                for (index in 0 until assets.length()) {
                    val asset = assets.optJSONObject(index) ?: continue
                    if (asset.optString("name") == ANDROID_ASSET_NAME) {
                        apkUrl = asset.optString("browser_download_url").takeIf { it.isNotBlank() }
                        break
                    }
                }
            }

            if (apkUrl == null) {
                return@withContext CheckResult.Error("The latest release does not contain $ANDROID_ASSET_NAME.")
            }

            CheckResult.Available(
                AvailableUpdate(
                    buildNumber = latestBuild,
                    tag = tag,
                    downloadUrl = apkUrl
                )
            )
        } catch (error: Exception) {
            CheckResult.Error(error.message ?: "Unable to check for updates.")
        }
    }

    suspend fun downloadAndInstall(update: AvailableUpdate): String {
        val apk = withContext(Dispatchers.IO) {
            try {
                val updatesDir = File(activity.cacheDir, "updates").apply { mkdirs() }
                val target = File(updatesDir, "Jarvis-${update.tag}.apk")
                val connection = (URL(update.downloadUrl).openConnection() as HttpURLConnection).apply {
                    connectTimeout = 15_000
                    readTimeout = 60_000
                    instanceFollowRedirects = true
                    setRequestProperty("User-Agent", "Jarvis-Android-Updater")
                }
                val responseCode = connection.responseCode
                if (responseCode !in 200..299) {
                    connection.disconnect()
                    return@withContext null
                }
                connection.inputStream.use { input ->
                    target.outputStream().use { output -> input.copyTo(output) }
                }
                connection.disconnect()
                if (target.length() < 1_000_000L) null else target
            } catch (_: Exception) {
                null
            }
        } ?: return "I couldn't download the update. Check your internet connection and try again."

        return withContext(Dispatchers.Main) {
            launchInstaller(apk)
        }
    }

    private fun launchInstaller(apk: File): String {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O &&
            !activity.packageManager.canRequestPackageInstalls()
        ) {
            val settingsIntent = Intent(
                Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                Uri.parse("package:${activity.packageName}")
            )
            activity.startActivity(settingsIntent)
            return "Allow Jarvis to install app updates, then return here and tap Update again."
        }

        val uri = FileProvider.getUriForFile(
            activity,
            "${activity.packageName}.fileprovider",
            apk
        )
        val intent = Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        activity.startActivity(intent)
        return "Update downloaded. Android is ready to install it; tap Update on the system installer."
    }

    private fun currentBuildNumber(): Int {
        val info = activity.packageManager.getPackageInfo(activity.packageName, 0)
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            info.longVersionCode.toInt()
        } else {
            @Suppress("DEPRECATION")
            info.versionCode
        }
    }

    companion object {
        private const val LATEST_RELEASE_API =
            "https://api.github.com/repos/Mhd-Azeem/Jarvis/releases/latest"
        private const val ANDROID_ASSET_NAME = "Jarvis-installable.apk"
    }
}
