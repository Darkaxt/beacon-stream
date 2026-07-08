package dev.beacon.android;

import android.content.SharedPreferences;

public final class SharedPreferencesLocalSettingsStorage implements BeaconLocalSettingsStorage {
    static final String KEY = "beacon.localSettings.v1";

    private final SharedPreferences preferences;

    public SharedPreferencesLocalSettingsStorage(SharedPreferences preferences) {
        this.preferences = preferences;
    }

    @Override
    public String read() {
        return preferences.getString(KEY, "");
    }

    @Override
    public void write(String json) {
        preferences.edit().putString(KEY, json == null ? "" : json).apply();
    }
}
