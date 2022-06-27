using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.UI;

public class CharacterAssetsDB : MonoBehaviour
{
    [Serializable]
    class CADB
    {
        public int number;
        public string name;
        public string name_1;
        public Sprite image;
    }

    [SerializeField]
    List<CADB> cADBs;

    CADB nowCADB;
    int nowIndex;

    [SerializeField]
    Image characterImage;

    [SerializeField]
    Text numText, nameText, name1Text;

    DateTime nowDateTime;
    

    // Start is called before the first frame update
    void Start()
    {
        ChangeCA();
        nowDateTime = DateTime.Now;
    }

    // Update is called once per frame
    void Update()
    {
        if(nowDateTime.Minute != DateTime.Now.Minute)
        {
            nowDateTime = DateTime.Now;
            ChangeCA(false);
        }
    }

    CADB ReturnRandomCADB()
    {
        nowIndex = Mathf.FloorToInt(UnityEngine.Random.value * cADBs.Count);
        return cADBs[nowIndex];
    }

    CADB ReturnNextCADB()
    {
        nowIndex = (nowIndex + 1) % cADBs.Count;
        return cADBs[nowIndex];
    }

    void ChangeCA(bool isRandom = true)
    {
        nowCADB = isRandom ? ReturnRandomCADB() : ReturnNextCADB();
        characterImage.sprite = nowCADB.image;
        numText.text = nowCADB.number.ToString("D3");
        nameText.text = nowCADB.name;
        name1Text.text = nowCADB.name_1;
    }
}
