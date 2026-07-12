package dev.beacon.android;

import java.util.concurrent.Executor;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

final class SerialInputBufferFeeder implements AutoCloseable {
    private final Object gate = new Object();
    private final Executor executor;
    private final Runnable shutdown;
    private int pending;
    private boolean closed;

    static SerialInputBufferFeeder system() {
        ExecutorService executor = Executors.newSingleThreadExecutor(command ->
            new Thread(command, "beacon-codec-input"));
        return new SerialInputBufferFeeder(executor, executor::shutdown);
    }

    SerialInputBufferFeeder(Executor executor, Runnable shutdown) {
        if (executor == null || shutdown == null) {
            throw new IllegalArgumentException("Input feeder dependencies are required.");
        }
        this.executor = executor;
        this.shutdown = shutdown;
    }

    boolean submit(Runnable work) {
        if (work == null) {
            throw new IllegalArgumentException("Input feeder work is required.");
        }
        synchronized (gate) {
            if (closed) return false;
            pending++;
        }
        try {
            executor.execute(() -> {
                try {
                    synchronized (gate) {
                        if (closed) return;
                    }
                    work.run();
                } finally {
                    completeOne();
                }
            });
            return true;
        } catch (RuntimeException failure) {
            completeOne();
            throw failure;
        }
    }

    @Override
    public void close() {
        boolean interrupted = false;
        synchronized (gate) {
            if (!closed) {
                closed = true;
                shutdown.run();
            }
            while (pending != 0) {
                try {
                    gate.wait();
                } catch (InterruptedException ignored) {
                    interrupted = true;
                }
            }
        }
        if (interrupted) Thread.currentThread().interrupt();
    }

    private void completeOne() {
        synchronized (gate) {
            pending--;
            if (pending == 0) gate.notifyAll();
        }
    }
}
