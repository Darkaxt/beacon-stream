package dev.beacon.android;

import org.junit.Test;
import org.w3c.dom.Document;
import org.w3c.dom.Node;

import java.io.File;

import javax.xml.parsers.DocumentBuilderFactory;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;

public final class AndroidManifestNetworkPolicyTest {
    private static final String AndroidNamespace = "http://schemas.android.com/apk/res/android";

    @Test
    public void allowsCleartextTrafficForLocalBeaconServer() throws Exception {
        File manifest = manifestFile();
        assertTrue("AndroidManifest.xml should exist at " + manifest.getAbsolutePath(), manifest.isFile());

        DocumentBuilderFactory factory = DocumentBuilderFactory.newInstance();
        factory.setNamespaceAware(true);
        Document document = factory.newDocumentBuilder().parse(manifest);

        Node cleartextAttribute = document
            .getDocumentElement()
            .getElementsByTagName("application")
            .item(0)
            .getAttributes()
            .getNamedItemNS(AndroidNamespace, "usesCleartextTraffic");

        assertNotNull("Application should allow cleartext traffic for the local Beacon server.", cleartextAttribute);
        String cleartext = cleartextAttribute.getNodeValue();
        assertEquals("true", cleartext);
    }

    private static File manifestFile() {
        File moduleRelative = new File("app/src/main/AndroidManifest.xml");
        if (moduleRelative.isFile()) {
            return moduleRelative;
        }

        return new File("src/main/AndroidManifest.xml");
    }
}
