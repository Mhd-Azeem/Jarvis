plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

val ciBuildNumber = System.getenv("GITHUB_RUN_NUMBER")?.toIntOrNull() ?: 1
val ciKeystorePath = System.getenv("JARVIS_KEYSTORE_PATH")

android {
    namespace = "com.azeem.jarvis"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.azeem.jarvis"
        minSdk = 26
        targetSdk = 36
        versionCode = ciBuildNumber
        versionName = "0.2.$ciBuildNumber"

        vectorDrawables {
            useSupportLibrary = true
        }
    }

    val jarvisSigning = if (!ciKeystorePath.isNullOrBlank()) {
        signingConfigs.create("jarvis") {
            storeFile = file(ciKeystorePath)
            storePassword = System.getenv("JARVIS_KEYSTORE_PASSWORD")
            keyAlias = System.getenv("JARVIS_KEY_ALIAS")
            keyPassword = System.getenv("JARVIS_KEY_PASSWORD")
        }
    } else {
        null
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = jarvisSigning
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    packaging {
        resources {
            excludes += "/META-INF/{AL2.0,LGPL2.1}"
        }
    }
}

dependencies {
    // Compose 1.11.x line: compatible with compileSdk 36 / AGP 8.13.x.
    // Compose 1.12.x requires API 37 and AGP 9.1+, so do not advance this BOM
    // until the Android build toolchain is upgraded together.
    val composeBom = platform("androidx.compose:compose-bom:2026.04.01")
    implementation(composeBom)
    androidTestImplementation(composeBom)

    implementation("androidx.core:core-ktx:1.17.0")
    implementation("androidx.activity:activity-compose:1.13.0")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.10.0")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-tooling-preview")
    debugImplementation("androidx.compose.ui:ui-tooling")
}
