#include <jni.h>

#include "Limelight.h"

JNIEXPORT jstring JNICALL
Java_dev_beacon_streaming_moonlight_MoonlightNativeCore_nativeIdentity(
    JNIEnv *env,
    jclass clazz) {
    (void) clazz;
    return (*env)->NewStringUTF(env, "moonlight-common-c");
}

JNIEXPORT jstring JNICALL
Java_dev_beacon_streaming_moonlight_MoonlightNativeCore_nativeStageName(
    JNIEnv *env,
    jclass clazz,
    jint stage) {
    (void) clazz;
    if (stage < STAGE_NONE || stage >= STAGE_MAX) {
        return NULL;
    }

    return (*env)->NewStringUTF(env, LiGetStageName(stage));
}
