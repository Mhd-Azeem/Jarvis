package com.azeem.jarvis.core

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
import java.util.zip.ZipInputStream

/** One-time, user-initiated model download. Recognition is fully local after installation. */
class WakeModel(private val context: Context) {
    private val folder = File(context.filesDir, "vosk-model-small-en-us-0.15")
    private val archiveUrl = "https://huggingface.co/rhasspy/vosk-models/resolve/e7ac2109d134b5f2404ba95389b2fb51916d4cab/en/vosk-model-small-en-us-0.15.zip"

    fun installed(): Boolean = File(folder, "am/final.mdl").isFile

    suspend fun install(onProgress: suspend (String) -> Unit): String = withContext(Dispatchers.IO) {
        if (installed()) return@withContext "Offline wake model is ready."
        val archive = File(context.cacheDir, "vosk-model.zip")
        val staging = File(context.filesDir, "wake-staging")
        try {
            onProgress("Downloading the 40 MB English offline speech model…")
            val connection = (URL(archiveUrl).openConnection() as HttpURLConnection).apply {
                connectTimeout = 20_000
                readTimeout = 60_000
                instanceFollowRedirects = true
            }
            if (connection.responseCode !in 200..299) error("Model download failed: HTTP ${connection.responseCode}")
            connection.inputStream.use { input -> archive.outputStream().use { output -> input.copyTo(output) } }
            connection.disconnect()
            if (archive.length() < 20_000_000 || archive.length() > 100_000_000) error("Model archive size is unexpected.")
            val digest = MessageDigest.getInstance("SHA-256")
            archive.inputStream().use { input ->
                val buffer = ByteArray(8192)
                while (true) {
                    val count = input.read(buffer)
                    if (count < 0) break
                    digest.update(buffer, 0, count)
                }
            }
            val hash = digest.digest().joinToString("") { "%02x".format(it) }
            if (!hash.equals("30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498", true))
                error("The downloaded model did not match the verified archive.")
            onProgress("Installing the offline speech model…")
            staging.deleteRecursively()
            staging.mkdirs()
            val rootPath = staging.canonicalPath + File.separator
            var total = 0L
            ZipInputStream(archive.inputStream().buffered()).use { zip ->
                while (true) {
                    val entry = zip.nextEntry ?: break
                    val target = File(staging, entry.name)
                    if (!target.canonicalPath.startsWith(rootPath)) error("Unsafe model archive entry.")
                    if (entry.isDirectory) target.mkdirs()
                    else {
                        target.parentFile?.mkdirs()
                        target.outputStream().use { output ->
                            val copied = zip.copyTo(output)
                            total += copied
                            if (total > 500_000_000) error("Model archive is too large.")
                        }
                    }
                    zip.closeEntry()
                }
            }
            val extracted = File(staging, folder.name)
            if (!File(extracted, "am/final.mdl").isFile) error("The downloaded model is incomplete.")
            if (folder.exists()) folder.deleteRecursively()
            if (!extracted.renameTo(folder)) error("Could not install the model.")
            "Offline wake model installed. Enable Hey Jarvis to start listening."
        } catch (error: Exception) {
            error.message ?: "Could not install the offline model."
        } finally {
            archive.delete()
            staging.deleteRecursively()
        }
    }
}
