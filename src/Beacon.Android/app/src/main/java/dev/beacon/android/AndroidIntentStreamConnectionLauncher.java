package dev.beacon.android;

import android.content.Context;
import android.content.Intent;
import android.net.Uri;

public final class AndroidIntentStreamConnectionLauncher implements StreamConnectionLauncher {
    private final Context context;

    public AndroidIntentStreamConnectionLauncher(Context context) {
        this.context = context;
    }

    @Override
    public void launch(String launchUri) {
        Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(launchUri));
        context.startActivity(intent);
    }
}
