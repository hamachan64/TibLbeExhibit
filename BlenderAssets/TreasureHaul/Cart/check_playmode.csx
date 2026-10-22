using UnityEngine;
using STYLY.TIB_Template.TreasureHaul;
var counter=UnityEngine.Object.FindFirstObjectByType<CartCargoCounter>();
var a=GameObject.Find("SampleCargo_0").GetComponent<CargoValuable>();
var b=GameObject.Find("SampleCargo_1").GetComponent<CargoValuable>();
var c=GameObject.Find("SampleCargo_2").GetComponent<CargoValuable>();
if(counter.TotalValue.CurrentValue!=3600)throw new System.Exception("Initial automatic sum must be 3600");
b.transform.position+=Vector3.right*2;
return "Moved 800 G cup out; next call must observe automatic 2800 G without RefreshNow";
