package com.azeem.jarvis.core

import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification

/** Reads notification titles only when Android has granted notification listener access. */
class JarvisNotificationListener : NotificationListenerService() {
    companion object {
        @Volatile var active: JarvisNotificationListener? = null
            private set
    }

    override fun onListenerConnected() { active = this }
    override fun onListenerDisconnected() { if (active === this) active = null }
    override fun onDestroy() {
        if (active === this) active = null
        super.onDestroy()
    }

    fun summary(): String {
        val items = try { activeNotifications?.takeLast(5).orEmpty() } catch (_: Exception) { emptyList() }
        if (items.isEmpty()) return "No recent notifications are available."
        return items.mapNotNull { notification ->
            val title = notification.notification.extras.getCharSequence("android.title")?.toString()
            val text = notification.notification.extras.getCharSequence("android.text")?.toString()
            if (title.isNullOrBlank() && text.isNullOrBlank()) null
            else listOfNotNull(title, text).joinToString(": ")
        }.joinToString(". ").ifBlank { "No readable notifications are available." }
    }
}
