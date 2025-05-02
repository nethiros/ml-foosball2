# Tischkicker-Agent mit ML-Agents und Echtzeit-Ballverfolgung

## Projektübersicht

Dieses Projekt zielt darauf ab, einen intelligenten Agenten mit Unity ML-Agents zu entwickeln, der Tischkicker spielen kann. Die entwickelten Modelle werden letztendlich eingesetzt, um Schrittmotoren anzusteuern, die die Stangen eines realen Tischkickers bewegen. Die Position des Balls wird dabei in Echtzeit über ein Python-Skript und eine Hochgeschwindigkeitskamera erfasst und via UDP an die Unity-Umgebung übertragen.

## Aktueller Entwicklungsstand

Aktuell konzentriert sich die Implementierung auf die Steuerung des Torwarts. Die Abwehrspieler-Implementierung ist noch in Bearbeitung und nicht vollständig.

## Projektstruktur

### Assets/Environments/TorwarTraining/
Dieser Ordner enthält die Hauptimplementierung des Torwart-Agenten, einschließlich:
- Trainingsumgebung
- Agent-Skripte
- Kollisionserkennung
- Belohnungsfunktionen

### Assets/Environments/Abwehr/
Dieser Ordner enthält die begonnene (aber noch unvollständige) Implementierung der Abwehrspieler.

### Assets/Scripts/
Dieser Ordner enthält unterstützende Skripte:
- Kamerasteuerung für die virtuelle Unity-Kamera
- UDP-Kommunikation zum Empfang der Ballkoordinaten
- Debugging-Werkzeuge zur Visualisierung und Analyse

### Assets/ML-Agents/Onnx/
Hier befindet sich die aktuelle ONNX-Datei des trainierten Torwart-Modells.

## Trainingsmethodik

### Reinforcement Learning
- **Algorithmus**: Proximal Policy Optimization (PPO)
- **Lernansatz**: Curriculum Learning
- **Konfiguration**: Siehe `curriculum.yaml` für detaillierte Parameter

### Trainingsablauf
1. Der Agent beginnt mit einfachen Szenarien
2. Die Schwierigkeit erhöht sich schrittweise basierend auf der Performance
3. Die Belohnungsfunktion berücksichtigt:
   - Erfolgreiche Torverteidigung
   - Effiziente Bewegung
   - Positionierung relativ zum Ball

## Datenfluss

Hochgeschwindigkeitskamera → Python-Verarbeitung → UDP-Übertragung → Unity-Umgebung → Agent-Entscheidung → Steuerungssignale

### Ballverfolgung
- Die Kamera erfasst Bilder des Spielfelds
- Python-Skripte verarbeiten die Bilder zur Erkennung der Ballposition
- Koordinaten werden über UDP an die Unity-Simulation übertragen

### Steuerung
- Der trainierte Agent trifft Entscheidungen basierend auf den Ballkoordinaten
- Die Ausgaben werden für die Ansteuerung von Schrittmotoren optimiert

## Nächste Schritte

1. Vervollständigung der Abwehrspieler-Implementierung
2. Integration weiterer Spielerelemente (Stürmer)
3. Optimierung des Modells für Echtzeitanforderungen
4. Kalibrierung der Schnittstelle zwischen virtueller Umgebung und realer Hardware

## Technische Details

- **Unity-Version**: [Version einfügen]
- **ML-Agents Version**: [Version einfügen]
- **Python-Abhängigkeiten**: OpenCV, Socket, NumPy
- **Hardware-Anforderungen**: Hochgeschwindigkeitskamera, Schrittmotorsteuerung

## Bekannte Probleme und Lösungsansätze

- Latenzprobleme bei der UDP-Übertragung
- Genauigkeit der Ballverfolgung bei hohen Geschwindigkeiten
- Übertragung von simulierten Bewegungen auf reale Motormechanik
