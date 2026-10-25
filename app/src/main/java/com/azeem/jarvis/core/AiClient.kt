package com.azeem.jarvis.core

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

data class ChatTurn(val role: String, val text: String)

/** OpenAI-compatible chat endpoint. Audio is never sent here. */
class AiClient {
    suspend fun reply(
        endpoint: String,
        apiKey: String,
        model: String,
        history: List<ChatTurn>,
        onChunk: suspend (String) -> Unit
    ): String = withContext(Dispatchers.IO) {
        require(endpoint.startsWith("https://")) { "Provider URL must use HTTPS." }
        require(apiKey.isNotBlank()) { "Configure an API key in Settings first." }
        val messages = JSONArray().apply {
            put(JSONObject().put("role", "system").put("content",
                "You are Jarvis, a concise personal assistant. Never claim to have performed a device action. Device actions are handled locally before this request."))
            history.takeLast(16).forEach { turn ->
                put(JSONObject().put("role", turn.role).put("content", turn.text))
            }
        }
        val body = JSONObject()
            .put("model", model)
            .put("stream", true)
            .put("messages", messages)
            .toString()
        val connection = (URL(endpoint).openConnection() as HttpURLConnection).apply {
            requestMethod = "POST"
            connectTimeout = 15_000
            readTimeout = 60_000
            doOutput = true
            setRequestProperty("Authorization", "Bearer $apiKey")
            setRequestProperty("Content-Type", "application/json")
            setRequestProperty("Accept", "text/event-stream")
        }
        try {
            connection.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
            if (connection.responseCode !in 200..299) {
                val detail = connection.errorStream?.bufferedReader()?.use { it.readText() }?.take(300)
                throw IllegalStateException("Provider error ${connection.responseCode}: ${detail ?: "no details"}")
            }
            val answer = StringBuilder()
            connection.inputStream.bufferedReader().useLines { lines ->
                lines.forEach { line ->
                    if (!line.startsWith("data: ")) return@forEach
                    val payload = line.removePrefix("data: ").trim()
                    if (payload == "[DONE]") return@forEach
                    val delta = runCatching {
                        JSONObject(payload).getJSONArray("choices").getJSONObject(0)
                            .getJSONObject("delta").optString("content")
                    }.getOrDefault("")
                    if (delta.isNotEmpty()) {
                        answer.append(delta)
                        kotlinx.coroutines.runBlocking { onChunk(answer.toString()) }
                    }
                }
            }
            answer.toString().ifBlank { "The provider returned an empty response." }
        } finally {
            connection.disconnect()
        }
    }
}
