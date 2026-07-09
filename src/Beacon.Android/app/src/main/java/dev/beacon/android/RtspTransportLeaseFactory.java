package dev.beacon.android;

public interface RtspTransportLeaseFactory {
    RtspTransportLease open(GameStreamEndpointPlan plan);
}
