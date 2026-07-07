package dev.beacon.android;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;

public final class BeaconJson {
    private static final Gson GSON = new GsonBuilder().serializeNulls().create();

    private BeaconJson() {
    }

    public static Gson gson() {
        return GSON;
    }
}
