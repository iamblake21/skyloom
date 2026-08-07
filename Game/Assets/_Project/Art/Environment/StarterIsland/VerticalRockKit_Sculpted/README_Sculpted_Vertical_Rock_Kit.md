# Sculpted Vertical Rock Kit

Kit modulare finale per costruire pareti rocciose verticali stylized. I prefab pronti all'uso sono in `Prefabs/` e condividono `Materials/M_VRKS_AutoGrass.mat`.

## Moduli finali

- `PF_VRKS_Straight_A`, `PF_VRKS_Straight_B`
- `PF_VRKS_Corner_A`, `PF_VRKS_Corner_B`
- `PF_VRKS_End_A`, `PF_VRKS_End_B`
- `PF_VRKS_Ledge_A`, `PF_VRKS_Transition_A`

Ogni prefab contiene un solo GameObject, una sola mesh continua, un MeshRenderer e un MeshCollider. I pivot sono centrati alla base per facilitare posizionamento, rotazione e scala.

## Uso consigliato

1. Trascinare i prefab dalla cartella `Prefabs/`.
2. Sovrapporre i moduli di circa il 30–45% e alternare scala, profondità e rotazione per nascondere le giunzioni.
3. Usare i moduli `End` per chiudere una parete, i `Corner` per cambiare direzione e `Ledge`/`Transition` per interrompere il profilo verticale.
4. Lasciare assegnato il materiale condiviso `M_VRKS_AutoGrass`.

## Shader automatico

Lo shader `CML/Environment/Vertical Rock Auto Grass` usa proiezione triplanare in spazio mondo. Il rock resta sulle superfici verticali, mentre l'erba appare sulle superfici rivolte verso l'alto. Ruotando una roccia, la maschera viene ricalcolata rispetto al world-up: non esiste alcun plane d'erba separato.

Il materiale `M_VRKS_DebugGrassMask` è solo diagnostico: bianco = erba, nero = roccia.

## Verifica

- 8 mesh finali uniche, watertight, una componente con 0 bordi non-manifold.
- Macro-normal per la selezione dell'erba memorizzata in UV2 e verificata su ogni mesh processata.
- Nessuna modifica applicata alla scena di gameplay, alla sua camera, agli alberi o all'illuminazione ambientale.

La scena `Preview/SCN_VRKS_Preview.unity` è un banco prova separato per catalogo, assemblaggio e test di rotazione.
