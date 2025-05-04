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




// Puffer
const int packetSize = 10;
uint8_t buffer[packetSize];


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

    alarmReaction(stepperR, 'r');
    
  }
  if (alarmStateL == LOW && alarmActiveL == LOW) {
    // Alarm erkannt (LOW wegen Pull-up)
    alarmActiveL = true;

    alarmReaction(stepperL, 'l');
    
  }
  

  if(Serial.available() >= packetSize){       //Warten bis das ankommende Packet mindestens so groß ist wie das Geforderte
    Serial.readBytes(buffer, packetSize);     //in dem Buffer schreiben

    if (checkChecksum(buffer, packetSize)) {  //Checksumme prüfen
      handleInput();
      Serial.write(0x06);                // ACK
      Serial.write(buffer[0]);
    } else {
      Serial.write(0x15);                // NACK
      Serial.write(buffer[0]);           // Optional: trotzdem Operation zurücksenden
    }

  }
}


//Verschiedene Strings handlen
void handleInput() {
  uint8_t opCode = buffer[0];
  uint16_t param1 = (buffer[1] << 8) | buffer[2];
  uint16_t param2 = (buffer[3] << 8) | buffer[4];
  uint16_t param3 = (buffer[5] << 8) | buffer[6];
  uint16_t param4 = (buffer[7] << 8) | buffer[8];


  switch (opCode) {
    case 1:  // Entspricht z.B. "P" (Positioniere beide)
      operation = 'P';
      setTargetsAndSpeeds(param1, param2, param3, param4);
      
      //Serial.println("Code1");
      break;

    case 2:  // Entspricht "L" (nur linker Motor)
      operation = 'L';
      setTargetAndSpeedL(param1, param2);
      break;

    case 3:  // Entspricht "R" (nur rechter Motor)
      operation = 'R';
      setTargetAndSpeedR(param3, param4);
      break;

     case 4:
      //Beschleunigungen müssen noch implementiert werde
      break;

    default:
      Serial.println("Error: Unknown opCode");
      break;
  }
}

bool checkChecksum(uint8_t *data, int length) {
  uint8_t checksum = 0;
  for (int i = 0; i < length - 1; i++) {
    checksum ^= data[i];
  }
  return checksum == data[length - 1];
}


void setMaxAccels(float accelL, float accelR){
  stepperL.setAcceleration(accelL);
  stepperR.setAcceleration(accelR);
}

void setTargetAndSpeedL(int target, float speed){
  stepperL.moveTo(target);
  stepperL.setMaxSpeed(speed);
}

void setTargetAndSpeedR(int target, float speed){
  stepperR.moveTo(target);
  stepperR.setMaxSpeed(speed);
}

void setTargetsAndSpeeds(int targetL, float speedL, int targetR, float speedR){
  stepperL.moveTo(targetL);
  stepperL.setMaxSpeed(speedL);
  stepperR.moveTo(targetR);
  stepperR.setMaxSpeed(speedR);
}

void setSpeedsAccels(float speedL, float accelL, float speedR, float accelR) {
  stepperL.setMaxSpeed(speedL);
  stepperL.setAcceleration(accelL);
  stepperR.setMaxSpeed(speedR); 
  stepperR.setAcceleration(accelR);
}

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

void resetAlarm(char stepper) {
  if(stepper == 'r'){
    digitalWrite(ENA_PIN_R, HIGH);  // Endstufe deaktivieren
    delay(1000);                  // 500ms warten
    digitalWrite(ENA_PIN_R, LOW); // Endstufe wieder aktivieren
    delay(200);                  // 200ms warten, bevor DIR-Signale gesendet werden (laut Datenblatt)
  }
  else if(stepper == 'l') {
    digitalWrite(ENA_PIN_L, HIGH);  // Endstufe deaktivieren
    delay(500);                  // 500ms warten
    digitalWrite(ENA_PIN_L, LOW); // Endstufe wieder aktivieren
    delay(200);                  // 200ms warten, bevor DIR-Signale gesendet werden (laut Datenblatt)
  }
}
