package com.azeem.jarvis.core

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class JarvisCommandProcessorTest {
    @Test fun localCommandsAreRoutedWithoutCloud() {
        listOf(
            "battery", "flashlight on", "torch off", "volume 40", "open YouTube",
            "home", "back", "recents", "read notifications", "scroll down",
            "tap Search", "type hello", "wifi settings", "set alarm 07:30", "play pause"
        ).forEach { assertTrue(it, JarvisCommandProcessor.supports(it)) }
    }

    @Test fun openConversationIsNotMistakenForDeviceCommand() {
        listOf("How are you?", "Explain quantum computing", "Write a poem")
            .forEach { assertFalse(it, JarvisCommandProcessor.supports(it)) }
    }
}
