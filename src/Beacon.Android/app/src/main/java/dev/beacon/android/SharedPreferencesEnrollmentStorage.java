package dev.beacon.android;

import android.content.SharedPreferences;

public final class SharedPreferencesEnrollmentStorage implements BeaconEnrollmentStorage {
    static final String KEY = "beacon.enrollment.v1";

    private final SharedPreferences preferences;

    public SharedPreferencesEnrollmentStorage(SharedPreferences preferences) {
        if (preferences == null) {
            throw new IllegalArgumentException("Beacon enrollment preferences are required.");
        }
        this.preferences = preferences;
    }

    @Override
    public String read() {
        return preferences.getString(KEY, "");
    }

    @Override
    public void write(String json) {
        if (!preferences.edit().putString(KEY, json == null ? "" : json).commit()) {
            throw new IllegalStateException("Beacon enrollment could not be persisted.");
        }
    }
}
