package dev.beacon.android;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.view.View;

public final class BeaconTestPatternView extends View {
    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private NativeStreamPresentation presentation = NativeStreamPresentation.none();

    public BeaconTestPatternView(Context context) {
        super(context);
        setMinimumHeight(360);
        setVisibility(GONE);
    }

    public void setPresentation(NativeStreamPresentation presentation) {
        this.presentation = presentation == null ? NativeStreamPresentation.none() : presentation;
        setVisibility(this.presentation.active() ? VISIBLE : GONE);
        invalidate();
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        if (!presentation.active()) {
            return;
        }

        int width = Math.max(getWidth(), 1);
        int height = Math.max(getHeight(), 1);
        int[] colors = BeaconTestPatternPalette.colorsFor(presentation.kind());
        int bandWidth = Math.max(width / colors.length, 1);
        for (int i = 0; i < colors.length; i++) {
            paint.setStyle(Paint.Style.FILL);
            paint.setColor(colors[i]);
            float left = i * bandWidth;
            float right = i == colors.length - 1 ? width : Math.min(width, left + bandWidth);
            canvas.drawRect(left, 0, right, height, paint);
        }

        paint.setColor(0xAA000000);
        canvas.drawRect(0, height - 92, width, height, paint);
        paint.setColor(Color.WHITE);
        paint.setTextSize(28);
        String label = presentation.label().isEmpty() ? "Beacon test stream" : presentation.label();
        canvas.drawText(label, 24, height - 52, paint);
        paint.setTextSize(18);
        canvas.drawText(presentation.endpointUri(), 24, height - 24, paint);
    }
}
