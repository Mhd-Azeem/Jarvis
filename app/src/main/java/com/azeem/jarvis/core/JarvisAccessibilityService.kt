package com.azeem.jarvis.core

import android.accessibilityservice.AccessibilityService
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo
import android.os.Bundle

/** Executes only explicit commands after the user enables Jarvis in Accessibility settings. */
class JarvisAccessibilityService : AccessibilityService() {
    companion object {
        @Volatile var active: JarvisAccessibilityService? = null
            private set
    }

    override fun onServiceConnected() { active = this }
    override fun onAccessibilityEvent(event: AccessibilityEvent?) = Unit
    override fun onInterrupt() = Unit
    override fun onDestroy() {
        if (active === this) active = null
        super.onDestroy()
    }

    fun execute(command: String): String {
        val lower = command.lowercase().trim()
        return when {
            lower == "back" -> global(GLOBAL_ACTION_BACK, "Going back.")
            lower == "home" -> global(GLOBAL_ACTION_HOME, "Going home.")
            lower == "recents" -> global(GLOBAL_ACTION_RECENTS, "Opening recent apps.")
            lower == "notifications" -> global(GLOBAL_ACTION_NOTIFICATIONS, "Opening notifications.")
            lower == "quick settings" -> global(GLOBAL_ACTION_QUICK_SETTINGS, "Opening quick settings.")
            lower == "scroll down" -> scroll(AccessibilityNodeInfo.ACTION_SCROLL_FORWARD)
            lower == "scroll up" -> scroll(AccessibilityNodeInfo.ACTION_SCROLL_BACKWARD)
            lower.startsWith("tap ") -> tap(command.substringAfter(' ').trim())
            lower.startsWith("type ") -> type(command.substringAfter(' '))
            else -> "That accessibility action is unavailable."
        }
    }

    private fun global(action: Int, success: String): String =
        if (performGlobalAction(action)) success else "Android did not allow that action."

    private fun scroll(action: Int): String {
        val node = find(rootInActiveWindow) { it.actionList.any { item -> item.id == action } }
            ?: return "No scrollable item is visible."
        return if (node.performAction(action)) "Scrolled." else "This screen did not scroll."
    }

    private fun tap(label: String): String {
        if (label.isBlank()) return "Tell me what visible item to tap."
        val node = find(rootInActiveWindow) {
            it.text?.toString()?.contains(label, ignoreCase = true) == true ||
                it.contentDescription?.toString()?.contains(label, ignoreCase = true) == true
        } ?: return "I couldn't find $label on the current screen."
        var clickable: AccessibilityNodeInfo? = node
        while (clickable != null && !clickable.isClickable) clickable = clickable.parent
        return if (clickable?.performAction(AccessibilityNodeInfo.ACTION_CLICK) == true) "Tapped $label." else "That item cannot be tapped."
    }

    private fun type(value: String): String {
        if (value.isBlank()) return "Tell me what to type."
        val node = find(rootInActiveWindow) { it.isEditable && it.isFocused }
            ?: find(rootInActiveWindow) { it.isEditable }
            ?: return "No editable field is visible."
        val arguments = Bundle().apply {
            putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, value)
        }
        return if (node.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, arguments)) "Typed into the visible field." else "Android did not allow typing there."
    }

    private fun find(root: AccessibilityNodeInfo?, predicate: (AccessibilityNodeInfo) -> Boolean): AccessibilityNodeInfo? {
        if (root == null) return null
        if (predicate(root)) return root
        for (index in 0 until root.childCount) {
            find(root.getChild(index), predicate)?.let { return it }
        }
        return null
    }
}
