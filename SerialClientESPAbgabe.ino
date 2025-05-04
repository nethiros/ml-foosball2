#include <AccelStepper.h>
#include <MultiStepper.h>


#define pulsePinR 16    //RX2
#define dirPinR 15      //D15

#define pulsePinL 18    //D18
#define dirPinL 17      //TX2

#define iniSensor 13    //D13
#define iniSensor2 14   //D14

#define alarmPinR 34    //D34
#define ENA_PIN_R 19    //D19

#define alarmPinL 35    //D35
#define ENA_PIN_L 21    //D21


//Zwei Schrittmotor-Instanzen (Linear, Rotation) initialisieren
AccelStepper stepperL(1, pulsePinL, dirPinL);
AccelStepper stepperR(1, pulsePinR, dirPinR);

int targetL = 400;
int targetR = 1600;
float speedL = 3000;
float speedR = 3500;
char operation = 'n';
unsigned long currentTime = 0;
float currentLSpeed = 0;
float currentRSpeed = 0;
bool alarmActiveR = false;
bool alarmActiveL = false;


void setup() {
  Serial.begin(115200);  // Nutzt die eingebaute USB-UART Verbindung
    while (!Serial) {
        ; // Warten, bis die serielle Verbindung bereit ist (nötig für einige Chips)
    }

  //Welcome Message an den TCP Server senden
  String message = "Abwehr";  // Anpassen für jede Achse (Torwart, Abwehr, Mittelfeld, Sturm)
  Serial.println(message);

  pinMode(iniSensor, INPUT);
  pinMode(alarmPinR, INPUT_PULLUP);
  pinMode(ENA_PIN_R, OUTPUT);
  digitalWrite(ENA_PIN_R, LOW);
  pinMode(alarmPinL, INPUT_PULLUP);
  pinMode(ENA_PIN_L, OUTPUT);
  digitalWrite(ENA_PIN_L, LOW);

  //Pulsbreite auf 25 Mikrosekunden. Standard von 2.5 ist zu kurz und fuehrt zu schlechten bewegungen und schrittverlust
  stepperL.setMinPulseWidth(25);
  stepperR.setMinPulseWidth(25);
  //Maximale Beschleunigungen setzen
  setMaxAccels(100000, 200000);
  //Initialtarget setzen
  setTargetsAndSpeeds(0, 500, 0, 500);
  
}

void loop() {
  stepperR.run();
  stepperL.run();
  
  bool alarmStateR = digitalRead(alarmPinR);
  bool alarmStateL = digitalRead(alarmPinL);

  if (alarmStateR == LOW && alarmActiveR == LOW) {
    // Alarm erkannt (LOW wegen Pull-up)
    alarmActiveR = true;

    alarmReaction(stepperR, 'r');     //Alarm Reaktion für Rotation durchführen
    
  }
  if (alarmStateL == LOW && alarmActiveL == LOW) {
    // Alarm erkannt (LOW wegen Pull-up)
    alarmActiveL = true;

    alarmReaction(stepperL, 'l');     //Alarm Reaktion für Linearbewegung durchführen
    
  }


  if (Serial.available()) {  // Falls Daten vom Laptop kommen
        String data = Serial.readStringUntil('\n');     //Daten lesen bis "\n"
        
        handleInput(data);                              //Daten auswerten
        Serial.println("Ack: " + data + "Op: " + operation);    //Rückmeldung senden
    }
}

//Funktion zum analysieren eines Strings, indem ein integer Wert zwischen zwei bestimmten Teilstringobjekten gefunden wird
int getValue(String data, String startDelimiter, String endDelimiter) {
  int startIndex = data.indexOf(startDelimiter);
  if (startIndex == -1) {
    return -1; // Delimiter nicht gefunden, gibt einen Fehlerwert zurück
  }
  startIndex += startDelimiter.length();
  int endIndex = data.indexOf(endDelimiter, startIndex);
  if (endIndex == -1) {
    return -1; // End-Delimiter nicht gefunden, gibt einen Fehlerwert zurück
  }

  String valueString = data.substring(startIndex, endIndex);
  return valueString.toInt(); // Konvertiert den extrahierten Wert in einen Integer und gibt ihn zurück
}

//Funktion zum Auswerten der erhaltenen Daten
void handleInput(String data){
  if(data.startsWith("TL")){        //Bewegungsanfrage für Linear und Rotation
    int targetL = getValue(data, "TL:", ":SL:");
    int speedL = getValue(data, "SL:", ":TR:");
    int targetR = getValue(data, "TR:", "SR");
    int speedR = getValue(data, "SR:", ":END");
    
    operation = 'P';
    setTargetsAndSpeeds(targetL, speedL, targetR, speedR);    //Zielwerte den extrahierten Parametern nach setzen
  }

  else if(data.startsWith("LINEAR")){               //Nur linear Bewegung
    int targetL = getValue(data, "TL:", ":SL:");
    int speedL = getValue(data, "SL:", ":END");
    operation = 'L';
    setTargetAndSpeedL(targetL, speedL);
  }

  else if(data.startsWith("ROTATION")){             //Nur rotation Bewegung
    int targetR = getValue(data, "TR:", ":SR:");
    int speedR = getValue(data, "SR:", ":END");
    operation = 'L';
    setTargetAndSpeedR(targetR, speedR);
  }

  else if(data.startsWith("MSL")){                  //Festlegen der maximalen Geschwindigkeiten und Beschleunigungen
    int maxSpeedStepL = getValue(data, "MSL:", ":MAL:");
    int maxAccelStepL = getValue(data, "MAL:", ":MSR:");
    int maxSpeedStepR = getValue(data, "MSR:", ":MAR:");
    int maxAccelStepR = getValue(data, "MAR:", ":END");
    operation = 'A';
    setSpeedsAccels(maxSpeedStepL, maxAccelStepL, maxSpeedStepR, maxAccelStepR);
    Serial.println(maxAccelStepL);
    Serial.println(maxAccelStepR);
  }

  else if(data=="POSITION"){                        //Abfrage der momentanen Position
    int posL = stepperL.currentPosition();
    int posR = stepperR.currentPosition();
    String pose = "L:" + String(posL) + ":R:" + String(posR);
    Serial.println(pose);
    operation = 'p';
  }
  else if(data=="HOME"){                      //Home Sequenz
    findHomePosition();
    operation = 'h';
  }
  else if(data=="STOP"){                      //Stoppe die momentane Bewegung
    stepperL.stop();
    stepperR.stop();
    operation = 's';
  }
  else{                                       //Keine umsetzbare Anfrage gefunden
    operation = 'N';
  }
}

//maximale Bescheunigung setzen
void setMaxAccels(float accelL, float accelR){
  stepperL.setAcceleration(accelL);
  stepperR.setAcceleration(accelR);
}

//lineares Ziel und Geschwindigkeit festlegen
void setTargetAndSpeedL(int target, float speed){
  stepperL.moveTo(target);
  stepperL.setMaxSpeed(speed);
}

//rotations Ziel und Geschwindigkeit festlegen
void setTargetAndSpeedR(int target, float speed){
  stepperR.moveTo(target);
  stepperR.setMaxSpeed(speed);
}

//lineares und rotations Ziel und Geschwindigkeit festlegen
void setTargetsAndSpeeds(int targetL, float speedL, int targetR, float speedR){
  stepperL.moveTo(targetL);
  stepperL.setMaxSpeed(speedL);
  stepperR.moveTo(targetR);
  stepperR.setMaxSpeed(speedR);
}

//Beschleunigung und GEschwindigkeit setzen
void setSpeedsAccels(float speedL, float accelL, float speedR, float accelR) {
  stepperL.setMaxSpeed(speedL);
  stepperL.setAcceleration(accelL);
  stepperR.setMaxSpeed(speedR); 
  stepperR.setAcceleration(accelR);
}

//Homing Sequenz
void findHomePosition(){
  stepperL.setMaxSpeed(500);
  while(digitalRead(iniSensor)){
    if(stepperL.distanceToGo()==0){
      stepperL.move(-1000);
    }
    stepperL.run();    
  }
  stepperL.stop();  
  stepperL.setCurrentPosition(0);

  stepperL.setMaxSpeed(500);
  stepperL.runToNewPosition(300);

  stepperL.setMaxSpeed(200);
  while(digitalRead(iniSensor)){
    if(stepperL.distanceToGo()==0){
      stepperL.move(-1000);
    }
    stepperL.run();    
  }
  stepperL.stop();  
  stepperL.setCurrentPosition(0);

  stepperL.setMaxSpeed(400);
  stepperL.runToNewPosition(200);  
}

//Alarm Reaktion
void alarmReaction(AccelStepper stepperMotor, char stepper){

  if(stepper == 'r'){
    long remainingDistance = stepperR.distanceToGo();
    stepperR.stop();
    resetAlarm(stepper);
    
  
    Serial.println("Alarm aktiviert! | Bewegung gestoppt! | Fahre entgegengesetzte Richtung! | remainingDistance:" + String(remainingDistance));
  
    stepperR.setMaxSpeed(500);
  
    if(remainingDistance >= 0){
      stepperR.move(-400);
      stepperR.runToPosition(); 
    }
    else if(remainingDistance < 0){
      stepperR.move(400);
      stepperR.runToPosition(); 
    }
    alarmActiveR = false;
  }
  
  else if(stepper == 'l'){
    long remainingDistance = stepperL.distanceToGo();
    stepperL.stop();
    resetAlarm(stepper);
    
  
    Serial.println("Alarm aktiviert! | Bewegung gestoppt! | Fahre entgegengesetzte Richtung! | remainingDistance:" + String(remainingDistance));
  
    stepperL.setMaxSpeed(500);
  
    if(remainingDistance >= 0){
      stepperL.move(-400);
      stepperL.runToPosition(); 
    }
    else if(remainingDistance < 0){
      stepperL.move(400);
      stepperL.runToPosition(); 
    }
    alarmActiveL = false;
  }

}

//Alarm der Endstufe zurücksetzen
void resetAlarm(char stepper) {
  if(stepper == 'r'){
    digitalWrite(ENA_PIN_R, HIGH);  //Endstufe deaktivieren
    delay(1000);                  //500ms warten
    digitalWrite(ENA_PIN_R, LOW); //Endstufe wieder aktivieren
    delay(200);                  //200ms warten, bevor DIR-Signale gesendet werden (laut Datenblatt)
  }
  else if(stepper == 'l') {
    digitalWrite(ENA_PIN_L, HIGH);  //Endstufe deaktivieren
    delay(500);                  //500ms warten
    digitalWrite(ENA_PIN_L, LOW); //Endstufe wieder aktivieren
    delay(200);                  //200ms warten, bevor DIR-Signale gesendet werden (laut Datenblatt)
  }
}
