package com.azeem.jarvis.service

import android.accessibilityservice.AccessibilityService
import android.os.Bundle
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo

class JarvisAccessibilityService : AccessibilityService() {

    companion object {
        @Volatile
        var instance: JarvisAccessibilityService? = null
            private set
    }

    override fun onServiceConnected() {
        super.onServiceConnected()
        instance = this
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) = Unit

    override fun onInterrupt() = Unit

    override fun onDestroy() {
        if (instance === this) instance = null
        super.onDestroy()
    }

    fun goBack(): Boolean = performGlobalAction(GLOBAL_ACTION_BACK)

    fun goHome(): Boolean = performGlobalAction(GLOBAL_ACTION_HOME)

    fun showRecents(): Boolean = performGlobalAction(GLOBAL_ACTION_RECENTS)

    fun showNotifications(): Boolean = performGlobalAction(GLOBAL_ACTION_NOTIFICATIONS)

    fun showQuickSettings(): Boolean = performGlobalAction(GLOBAL_ACTION_QUICK_SETTINGS)

    fun scrollForward(): Boolean = performOnFirstScrollable(AccessibilityNodeInfo.ACTION_SCROLL_FORWARD)

    fun scrollBackward(): Boolean = performOnFirstScrollable(AccessibilityNodeInfo.ACTION_SCROLL_BACKWARD)

    fun clickText(text: String): Boolean {
        val root = rootInActiveWindow ?: return false
        val nodes = root.findAccessibilityNodeInfosByText(text)
        for (node in nodes) {
            var target: AccessibilityNodeInfo? = node
            while (target != null) {
                if (target.isClickable && target.performAction(AccessibilityNodeInfo.ACTION_CLICK)) return true
                target = target.parent
            }
        }
        return false
    }

    fun setText(text: String): Boolean {
        val root = rootInActiveWindow ?: return false
        val editable = findEditable(root) ?: return false
        val args = Bundle().apply {
            putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, text)
        }
        return editable.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, args)
    }

    private fun performOnFirstScrollable(action: Int): Boolean {
        val root = rootInActiveWindow ?: return false
        val queue = ArrayDeque<AccessibilityNodeInfo>()
        queue.add(root)
        while (queue.isNotEmpty()) {
            val node = queue.removeFirst()
            if (node.isScrollable && node.performAction(action)) return true
            for (i in 0 until node.childCount) {
                node.getChild(i)?.let(queue::add)
            }
        }
        return false
    }

    private fun findEditable(node: AccessibilityNodeInfo): AccessibilityNodeInfo? {
        if (node.isEditable && node.isVisibleToUser) return node
        for (i in 0 until node.childCount) {
            node.getChild(i)?.let { child ->
                findEditable(child)?.let { return it }
            }
        }
        return null
    }
}
