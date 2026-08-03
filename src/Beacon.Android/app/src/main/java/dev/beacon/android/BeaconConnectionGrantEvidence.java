package dev.beacon.android;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.Arrays;
import java.util.Base64;

record BeaconConnectionGrantEvidence(String sessionId, String ticketFingerprint) {
    static BeaconConnectionGrantEvidence fromJson(String responseBody) {
        byte[] ticket = null;
        try {
            JsonObject connection = JsonParser.parseString(responseBody)
                .getAsJsonObject()
                .getAsJsonObject("connection");
            if (connection == null) {
                throw new IllegalArgumentException("Connection grant is required.");
            }
            String sessionId = requiredText(connection, "sessionId");
            ticket = Base64.getDecoder().decode(requiredText(connection, "ticket"));
            if (ticket.length == 0) {
                throw new IllegalArgumentException("Connection grant ticket is empty.");
            }
            return new BeaconConnectionGrantEvidence(sessionId, sha256(ticket));
        } catch (IllegalArgumentException error) {
            throw error;
        } catch (RuntimeException error) {
            throw new IllegalArgumentException("Connection grant evidence is invalid.", error);
        } finally {
            if (ticket != null) Arrays.fill(ticket, (byte) 0);
        }
    }

    private static String requiredText(JsonObject value, String name) {
        if (!value.has(name) || value.get(name).isJsonNull()) {
            throw new IllegalArgumentException("Connection grant " + name + " is required.");
        }
        String text = value.get(name).getAsString().trim();
        if (text.isEmpty()) {
            throw new IllegalArgumentException("Connection grant " + name + " is required.");
        }
        return text;
    }

    private static String sha256(byte[] value) {
        try {
            byte[] digest = MessageDigest.getInstance("SHA-256").digest(value);
            try {
                StringBuilder hex = new StringBuilder(digest.length * 2);
                for (byte item : digest) hex.append(String.format("%02X", item & 0xff));
                return hex.toString();
            } finally {
                Arrays.fill(digest, (byte) 0);
            }
        } catch (NoSuchAlgorithmException error) {
            throw new IllegalStateException("SHA-256 is unavailable.", error);
        }
    }

    @Override
    public String toString() {
        return "session=" + sessionId + " ticketSha256=" + ticketFingerprint;
    }
}
