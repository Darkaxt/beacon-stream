package dev.beacon.android;

import org.junit.Test;

import java.util.List;

import static org.junit.Assert.assertEquals;

public final class BeaconGameCatalogTest {
    @Test
    public void parseReturnsTypedGameEntries() {
        List<BeaconGameCatalog.GameEntry> games = BeaconGameCatalog.parse(
            "{\"games\":["
                + "{\"id\":\"steam-shortcut:3767414131\",\"title\":\"Dispatch\",\"source\":\"steam-shortcut\",\"installed\":true,"
                + "\"launch\":{\"type\":\"steam-rungameid\",\"command\":\"steam://rungameid/16180920483166814208\"}},"
                + "{\"id\":\"heroic:persona-5\",\"title\":\"Persona 5\",\"source\":\"heroic\",\"installed\":false,"
                + "\"launch\":{\"type\":\"process\",\"command\":\"D:/Games/P5/p5.exe\"}}"
                + "]}");

        assertEquals(2, games.size());
        assertEquals("steam-shortcut:3767414131", games.get(0).id());
        assertEquals("Dispatch", games.get(0).title());
        assertEquals("steam-shortcut", games.get(0).source());
        assertEquals("steam-rungameid", games.get(0).launchType());
        assertEquals("Dispatch [steam-shortcut] steam-shortcut:3767414131 installed", games.get(0).displayLabel());
        assertEquals("Persona 5 [heroic] heroic:persona-5 not installed", games.get(1).displayLabel());
    }

    @Test
    public void parseEmptyCatalogReturnsEmptyList() {
        assertEquals(0, BeaconGameCatalog.parse("{\"games\":[]}").size());
    }

    @Test
    public void summarizeUsesParsedEntries() {
        String summary = BeaconGameCatalog.summarize(
            "{\"games\":[{\"id\":\"manual:game\",\"title\":\"Manual Game\",\"source\":\"manual\",\"installed\":true}]}");

        assertEquals("Manual Game [manual] manual:game installed", summary);
    }
}
