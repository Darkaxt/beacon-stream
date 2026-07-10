#include <jni.h>
#include <pthread.h>
#include <string.h>

#include "Limelight.h"

static JavaVM *Jvm;
static pthread_key_t JniEnvKey;
static pthread_once_t JniEnvKeyOnce = PTHREAD_ONCE_INIT;

static jobject VideoRenderer;
static jobject ConnectionListener;
static jbyteArray FrameBuffer;

static jmethodID RendererSetupMethod;
static jmethodID RendererStartMethod;
static jmethodID RendererStopMethod;
static jmethodID RendererCleanupMethod;
static jmethodID RendererSubmitMethod;
static jmethodID ListenerStageStartingMethod;
static jmethodID ListenerStageCompleteMethod;
static jmethodID ListenerStageFailedMethod;
static jmethodID ListenerConnectionStartedMethod;
static jmethodID ListenerConnectionTerminatedMethod;
static jmethodID ListenerConnectionStatusMethod;
static jmethodID ListenerHdrModeMethod;

static void detach_thread(void *context) {
    (void) context;
    (*Jvm)->DetachCurrentThread(Jvm);
}

static void initialize_jni_env_key(void) {
    pthread_key_create(&JniEnvKey, detach_thread);
}

static JNIEnv *get_thread_env(void) {
    JNIEnv *env;
    if ((*Jvm)->GetEnv(Jvm, (void **) &env, JNI_VERSION_1_6) == JNI_OK) {
        return env;
    }

    pthread_once(&JniEnvKeyOnce, initialize_jni_env_key);
    env = pthread_getspecific(JniEnvKey);
    if (env != NULL) {
        return env;
    }

    if ((*Jvm)->AttachCurrentThread(Jvm, &env, NULL) != JNI_OK) {
        return NULL;
    }
    pthread_setspecific(JniEnvKey, env);
    return env;
}

static bool clear_pending_exception(JNIEnv *env) {
    if (env == NULL || !(*env)->ExceptionCheck(env)) {
        return false;
    }

    (*env)->ExceptionClear(env);
    return true;
}

static void release_callback_references(JNIEnv *env) {
    if (FrameBuffer != NULL) {
        (*env)->DeleteGlobalRef(env, FrameBuffer);
        FrameBuffer = NULL;
    }
    if (VideoRenderer != NULL) {
        (*env)->DeleteGlobalRef(env, VideoRenderer);
        VideoRenderer = NULL;
    }
    if (ConnectionListener != NULL) {
        (*env)->DeleteGlobalRef(env, ConnectionListener);
        ConnectionListener = NULL;
    }
}

static int video_setup(int video_format, int width, int height, int fps, void *context, int flags) {
    (void) context;
    (void) flags;
    JNIEnv *env = get_thread_env();
    if (env == NULL || VideoRenderer == NULL) {
        return -1;
    }

    jint result = (*env)->CallIntMethod(
        env,
        VideoRenderer,
        RendererSetupMethod,
        video_format,
        width,
        height,
        fps);
    if (clear_pending_exception(env) || result != 0) {
        return result == 0 ? -1 : result;
    }

    if (FrameBuffer != NULL) {
        (*env)->DeleteGlobalRef(env, FrameBuffer);
    }
    jbyteArray local_buffer = (*env)->NewByteArray(env, 32768);
    if (local_buffer == NULL || clear_pending_exception(env)) {
        FrameBuffer = NULL;
        return -1;
    }
    FrameBuffer = (*env)->NewGlobalRef(env, local_buffer);
    (*env)->DeleteLocalRef(env, local_buffer);
    return FrameBuffer == NULL ? -1 : 0;
}

static void video_start(void) {
    JNIEnv *env = get_thread_env();
    if (env != NULL && VideoRenderer != NULL) {
        (*env)->CallVoidMethod(env, VideoRenderer, RendererStartMethod);
        clear_pending_exception(env);
    }
}

static void video_stop(void) {
    JNIEnv *env = get_thread_env();
    if (env != NULL && VideoRenderer != NULL) {
        (*env)->CallVoidMethod(env, VideoRenderer, RendererStopMethod);
        clear_pending_exception(env);
    }
}

static void video_cleanup(void) {
    JNIEnv *env = get_thread_env();
    if (env == NULL) {
        return;
    }

    if (FrameBuffer != NULL) {
        (*env)->DeleteGlobalRef(env, FrameBuffer);
        FrameBuffer = NULL;
    }
    if (VideoRenderer != NULL) {
        (*env)->CallVoidMethod(env, VideoRenderer, RendererCleanupMethod);
        clear_pending_exception(env);
    }
}

static int ensure_frame_capacity(JNIEnv *env, int required_length) {
    if (FrameBuffer != NULL && (*env)->GetArrayLength(env, FrameBuffer) >= required_length) {
        return 0;
    }

    if (FrameBuffer != NULL) {
        (*env)->DeleteGlobalRef(env, FrameBuffer);
        FrameBuffer = NULL;
    }
    jbyteArray local_buffer = (*env)->NewByteArray(env, required_length);
    if (local_buffer == NULL || clear_pending_exception(env)) {
        return -1;
    }
    FrameBuffer = (*env)->NewGlobalRef(env, local_buffer);
    (*env)->DeleteLocalRef(env, local_buffer);
    return FrameBuffer == NULL ? -1 : 0;
}

static int submit_buffer(
    JNIEnv *env,
    int length,
    int buffer_type,
    PDECODE_UNIT decode_unit) {
    jint result = (*env)->CallIntMethod(
        env,
        VideoRenderer,
        RendererSubmitMethod,
        FrameBuffer,
        length,
        buffer_type,
        decode_unit->frameType,
        decode_unit->frameNumber,
        (jlong) decode_unit->presentationTimeUs);
    return clear_pending_exception(env) ? DR_NEED_IDR : result;
}

static int video_submit_decode_unit(PDECODE_UNIT decode_unit) {
    JNIEnv *env = get_thread_env();
    if (env == NULL || VideoRenderer == NULL || decode_unit == NULL || decode_unit->fullLength <= 0) {
        return DR_NEED_IDR;
    }
    if (ensure_frame_capacity(env, decode_unit->fullLength) != 0) {
        return DR_NEED_IDR;
    }

    for (PLENTRY entry = decode_unit->bufferList; entry != NULL; entry = entry->next) {
        if (entry->bufferType == BUFFER_TYPE_PICDATA) {
            continue;
        }

        (*env)->SetByteArrayRegion(env, FrameBuffer, 0, entry->length, (const jbyte *) entry->data);
        if (clear_pending_exception(env)) {
            return DR_NEED_IDR;
        }
        int result = submit_buffer(env, entry->length, entry->bufferType, decode_unit);
        if (result != DR_OK) {
            return result;
        }
    }

    int picture_offset = 0;
    for (PLENTRY entry = decode_unit->bufferList; entry != NULL; entry = entry->next) {
        if (entry->bufferType != BUFFER_TYPE_PICDATA) {
            continue;
        }

        (*env)->SetByteArrayRegion(
            env,
            FrameBuffer,
            picture_offset,
            entry->length,
            (const jbyte *) entry->data);
        if (clear_pending_exception(env)) {
            return DR_NEED_IDR;
        }
        picture_offset += entry->length;
    }

    return picture_offset == 0
        ? DR_OK
        : submit_buffer(env, picture_offset, BUFFER_TYPE_PICDATA, decode_unit);
}

static void listener_stage_starting(int stage) {
    JNIEnv *env = get_thread_env();
    if (env != NULL && ConnectionListener != NULL) {
        (*env)->CallVoidMethod(env, ConnectionListener, ListenerStageStartingMethod, stage);
        clear_pending_exception(env);
    }
}

static void listener_stage_complete(int stage) {
    JNIEnv *env = get_thread_env();
    if (env != NULL && ConnectionListener != NULL) {
        (*env)->CallVoidMethod(env, ConnectionListener, ListenerStageCompleteMethod, stage);
        clear_pending_exception(env);
    }
}

static void listener_stage_failed(int stage, int error_code) {
    JNIEnv *env = get_thread_env();
    if (env != NULL && ConnectionListener != NULL) {
        (*env)->CallVoidMethod(env, ConnectionListener, ListenerStageFailedMethod, stage, error_code);
        clear_pending_exception(env);
    }
}

static void listener_connection_started(void) {
    JNIEnv *env = get_thread_env();
    if (env != NULL && ConnectionListener != NULL) {
        (*env)->CallVoidMethod(env, ConnectionListener, ListenerConnectionStartedMethod);
        clear_pending_exception(env);
    }
}

static void listener_connection_terminated(int error_code) {
    JNIEnv *env = get_thread_env();
    if (env != NULL && ConnectionListener != NULL) {
        (*env)->CallVoidMethod(env, ConnectionListener, ListenerConnectionTerminatedMethod, error_code);
        clear_pending_exception(env);
    }
}

static void listener_connection_status_update(int status) {
    JNIEnv *env = get_thread_env();
    if (env != NULL && ConnectionListener != NULL) {
        (*env)->CallVoidMethod(env, ConnectionListener, ListenerConnectionStatusMethod, status);
        clear_pending_exception(env);
    }
}

static void listener_set_hdr_mode(bool enabled) {
    JNIEnv *env = get_thread_env();
    if (env != NULL && ConnectionListener != NULL) {
        (*env)->CallVoidMethod(env, ConnectionListener, ListenerHdrModeMethod, enabled ? JNI_TRUE : JNI_FALSE);
        clear_pending_exception(env);
    }
}

static int cache_callback_methods(JNIEnv *env, jobject renderer, jobject listener) {
    jclass renderer_class = (*env)->GetObjectClass(env, renderer);
    jclass listener_class = (*env)->GetObjectClass(env, listener);
    if (renderer_class == NULL || listener_class == NULL) {
        clear_pending_exception(env);
        return -1;
    }

    RendererSetupMethod = (*env)->GetMethodID(env, renderer_class, "setup", "(IIII)I");
    RendererStartMethod = (*env)->GetMethodID(env, renderer_class, "start", "()V");
    RendererStopMethod = (*env)->GetMethodID(env, renderer_class, "stop", "()V");
    RendererCleanupMethod = (*env)->GetMethodID(env, renderer_class, "cleanup", "()V");
    RendererSubmitMethod = (*env)->GetMethodID(env, renderer_class, "submitDecodeUnit", "([BIIIIJ)I");
    ListenerStageStartingMethod = (*env)->GetMethodID(env, listener_class, "stageStarting", "(I)V");
    ListenerStageCompleteMethod = (*env)->GetMethodID(env, listener_class, "stageComplete", "(I)V");
    ListenerStageFailedMethod = (*env)->GetMethodID(env, listener_class, "stageFailed", "(II)V");
    ListenerConnectionStartedMethod = (*env)->GetMethodID(env, listener_class, "connectionStarted", "()V");
    ListenerConnectionTerminatedMethod = (*env)->GetMethodID(env, listener_class, "connectionTerminated", "(I)V");
    ListenerConnectionStatusMethod = (*env)->GetMethodID(env, listener_class, "connectionStatusUpdate", "(I)V");
    ListenerHdrModeMethod = (*env)->GetMethodID(env, listener_class, "setHdrMode", "(Z)V");

    (*env)->DeleteLocalRef(env, renderer_class);
    (*env)->DeleteLocalRef(env, listener_class);
    return clear_pending_exception(env) ? -1 : 0;
}

JNIEXPORT jint JNICALL JNI_OnLoad(JavaVM *vm, void *reserved) {
    (void) reserved;
    Jvm = vm;
    return JNI_VERSION_1_6;
}

JNIEXPORT jstring JNICALL
Java_dev_beacon_streaming_moonlight_MoonlightNativeConnection_nativeConnectionIdentity(
    JNIEnv *env,
    jclass clazz) {
    (void) clazz;
    return (*env)->NewStringUTF(env, "LiStartConnection");
}

JNIEXPORT jint JNICALL
Java_dev_beacon_streaming_moonlight_MoonlightNativeConnection_nativeStart(
    JNIEnv *env,
    jclass clazz,
    jstring address,
    jstring app_version,
    jstring gfe_version,
    jstring rtsp_session_url,
    jint server_codec_mode_support,
    jint width,
    jint height,
    jint fps,
    jint bitrate,
    jint packet_size,
    jint streaming_remotely,
    jint audio_configuration,
    jint supported_video_formats,
    jint client_refresh_rate_x100,
    jint color_space,
    jint color_range,
    jint encryption_flags,
    jbyteArray remote_input_key,
    jbyteArray remote_input_iv,
    jint video_capabilities,
    jobject renderer,
    jobject listener) {
    (void) clazz;
    if (address == NULL || app_version == NULL || rtsp_session_url == NULL ||
        remote_input_key == NULL || remote_input_iv == NULL || renderer == NULL || listener == NULL ||
        (*env)->GetArrayLength(env, remote_input_key) != 16 ||
        (*env)->GetArrayLength(env, remote_input_iv) != 16 ||
        VideoRenderer != NULL || ConnectionListener != NULL) {
        return -1;
    }
    if (cache_callback_methods(env, renderer, listener) != 0) {
        return -1;
    }

    VideoRenderer = (*env)->NewGlobalRef(env, renderer);
    ConnectionListener = (*env)->NewGlobalRef(env, listener);
    if (VideoRenderer == NULL || ConnectionListener == NULL) {
        release_callback_references(env);
        return -1;
    }

    SERVER_INFORMATION server_info;
    STREAM_CONFIGURATION stream_config;
    CONNECTION_LISTENER_CALLBACKS connection_callbacks;
    DECODER_RENDERER_CALLBACKS video_callbacks;
    LiInitializeServerInformation(&server_info);
    LiInitializeStreamConfiguration(&stream_config);
    LiInitializeConnectionCallbacks(&connection_callbacks);
    LiInitializeVideoCallbacks(&video_callbacks);

    const char *address_chars = (*env)->GetStringUTFChars(env, address, NULL);
    const char *app_version_chars = (*env)->GetStringUTFChars(env, app_version, NULL);
    const char *gfe_version_chars = gfe_version == NULL
        ? NULL
        : (*env)->GetStringUTFChars(env, gfe_version, NULL);
    const char *rtsp_session_url_chars = (*env)->GetStringUTFChars(env, rtsp_session_url, NULL);
    if (address_chars == NULL || app_version_chars == NULL || rtsp_session_url_chars == NULL ||
        (gfe_version != NULL && gfe_version_chars == NULL)) {
        if (address_chars != NULL) {
            (*env)->ReleaseStringUTFChars(env, address, address_chars);
        }
        if (app_version_chars != NULL) {
            (*env)->ReleaseStringUTFChars(env, app_version, app_version_chars);
        }
        if (gfe_version_chars != NULL) {
            (*env)->ReleaseStringUTFChars(env, gfe_version, gfe_version_chars);
        }
        if (rtsp_session_url_chars != NULL) {
            (*env)->ReleaseStringUTFChars(env, rtsp_session_url, rtsp_session_url_chars);
        }
        clear_pending_exception(env);
        release_callback_references(env);
        return -1;
    }

    server_info.address = address_chars;
    server_info.serverInfoAppVersion = app_version_chars;
    server_info.serverInfoGfeVersion = gfe_version_chars;
    server_info.rtspSessionUrl = rtsp_session_url_chars;
    server_info.serverCodecModeSupport = server_codec_mode_support;

    stream_config.width = width;
    stream_config.height = height;
    stream_config.fps = fps;
    stream_config.bitrate = bitrate;
    stream_config.packetSize = packet_size;
    stream_config.streamingRemotely = streaming_remotely;
    stream_config.audioConfiguration = audio_configuration;
    stream_config.supportedVideoFormats = supported_video_formats;
    stream_config.clientRefreshRateX100 = client_refresh_rate_x100;
    stream_config.colorSpace = color_space;
    stream_config.colorRange = color_range;
    stream_config.encryptionFlags = encryption_flags;
    (*env)->GetByteArrayRegion(env, remote_input_key, 0, 16, (jbyte *) stream_config.remoteInputAesKey);
    (*env)->GetByteArrayRegion(env, remote_input_iv, 0, 16, (jbyte *) stream_config.remoteInputAesIv);
    if (clear_pending_exception(env)) {
        (*env)->ReleaseStringUTFChars(env, address, address_chars);
        (*env)->ReleaseStringUTFChars(env, app_version, app_version_chars);
        if (gfe_version_chars != NULL) {
            (*env)->ReleaseStringUTFChars(env, gfe_version, gfe_version_chars);
        }
        (*env)->ReleaseStringUTFChars(env, rtsp_session_url, rtsp_session_url_chars);
        release_callback_references(env);
        return -1;
    }

    connection_callbacks.stageStarting = listener_stage_starting;
    connection_callbacks.stageComplete = listener_stage_complete;
    connection_callbacks.stageFailed = listener_stage_failed;
    connection_callbacks.connectionStarted = listener_connection_started;
    connection_callbacks.connectionTerminated = listener_connection_terminated;
    connection_callbacks.connectionStatusUpdate = listener_connection_status_update;
    connection_callbacks.setHdrMode = listener_set_hdr_mode;

    video_callbacks.setup = video_setup;
    video_callbacks.start = video_start;
    video_callbacks.stop = video_stop;
    video_callbacks.cleanup = video_cleanup;
    video_callbacks.submitDecodeUnit = video_submit_decode_unit;
    video_callbacks.capabilities = video_capabilities;

    int result = LiStartConnection(
        &server_info,
        &stream_config,
        &connection_callbacks,
        &video_callbacks,
        NULL,
        NULL,
        0,
        NULL,
        0);

    (*env)->ReleaseStringUTFChars(env, address, address_chars);
    (*env)->ReleaseStringUTFChars(env, app_version, app_version_chars);
    if (gfe_version_chars != NULL) {
        (*env)->ReleaseStringUTFChars(env, gfe_version, gfe_version_chars);
    }
    (*env)->ReleaseStringUTFChars(env, rtsp_session_url, rtsp_session_url_chars);

    if (result != 0) {
        release_callback_references(env);
    }
    return result;
}

JNIEXPORT void JNICALL
Java_dev_beacon_streaming_moonlight_MoonlightNativeConnection_nativeStop(
    JNIEnv *env,
    jclass clazz) {
    (void) clazz;
    LiStopConnection();
    release_callback_references(env);
}

JNIEXPORT void JNICALL
Java_dev_beacon_streaming_moonlight_MoonlightNativeConnection_nativeInterrupt(
    JNIEnv *env,
    jclass clazz) {
    (void) env;
    (void) clazz;
    LiInterruptConnection();
}
