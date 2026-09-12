package com.azeem.jarvis.service

import android.app.Notification
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import java.util.concurrent.CopyOnWriteArrayList

class JarvisNotificationListener : NotificationListenerService() {

    data class NotificationSnapshot(
        val key: String,
        val packageName: String,
        val title: String,
        val text: String,
        val postedAt: Long
    )

    companion object {
        private val items = CopyOnWriteArrayList<NotificationSnapshot>()

        fun recent(limit: Int = 10): List<NotificationSnapshot> =
            items.sortedByDescending { it.postedAt }.take(limit)
    }

    override fun onNotificationPosted(sbn: StatusBarNotification?) {
        sbn ?: return
        val extras = sbn.notification.extras
        val title = extras.getCharSequence(Notification.EXTRA_TITLE)?.toString().orEmpty()
        val text = extras.getCharSequence(Notification.EXTRA_TEXT)?.toString().orEmpty()

        items.removeAll { it.key == sbn.key }
        items.add(
            NotificationSnapshot(
                key = sbn.key,
                packageName = sbn.packageName,
                title = title,
                text = text,
                postedAt = sbn.postTime
            )
        )
        while (items.size > 50) items.removeAt(0)
    }

    override fun onNotificationRemoved(sbn: StatusBarNotification?) {
        sbn ?: return
        items.removeAll { it.key == sbn.key }
    }
}
