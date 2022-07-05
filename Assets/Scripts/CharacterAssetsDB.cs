using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.IO;

public class CharacterAssetsDB : MonoBehaviour
{
    [Serializable]
    class CADB
    {
        public int number;
        public string name;
        public string name_1;
        public string name_jp;
        public string gender;
        public string classname;
        public int height;
        public string age;
        public Sprite image;
    }

    [SerializeField]
    List<CADB> cADBs, loadedCADBs;

    CADB nowCADB;
    int nowIndex = -1;
    int numAnim;
    float alphaAnim = 0f;
    
    [SerializeField]
    int numAnimDuration = 300;
    [SerializeField]
    float acceralation = 2.65f, animationTime = 1.4f;

    [SerializeField]
    Color textColor = new Color(50f / 255f, 50f / 255f, 50f / 255f);

    [SerializeField]
    Image characterImage;

    [SerializeField]
    Text numText, nameText, name1Text, nameJpText, genderText, classnameText, heightText, ageText, clockText;

    DateTime nowDateTime;

    TextAsset csvFile;
    List<string[]> csvDatas = new List<string[]>();

    [SerializeField]
    bool isSaveCADB2CSV = false, isLoadCSV2CADB = false;

    // Start is called before the first frame update
    void Start()
    {
        if (isSaveCADB2CSV) _ExportCADBs2CSV();
        if (isLoadCSV2CADB) _LoadCSV2CADBs();
        SetNowDateTime();
        StartCoroutine(AnimateCADB(true));
    }

    // Update is called once per frame
    void Update()
    {
        if(nowDateTime.Minute != DateTime.Now.Minute)
        {
            SetNowDateTime();
            StartCoroutine(AnimateCADB(false));
        }
    }

    // https://qiita.com/mino4273/items/cf0b3bdbdb66b774ab23
    private void _ExportCADBs2CSV()
    {
        StreamWriter sw = new StreamWriter("./Assets/Resources/CADBs.csv", false);

        foreach (CADB i in cADBs)
        {
            string _buff = i.number.ToString() + "," + i.name + "," + i.name_1 + "," + i.name_jp + ","
                + i.gender + "," + i.classname + "," + i.height.ToString() + "," + i.age + ","
                + i.image.name;
            sw.WriteLine(_buff);
        }

        sw.Flush();
        sw.Close();
    }

    // https://note.com/macgyverthink/n/n83943f3bad60
    private void _LoadCSV2CADBs()
    {
        csvFile = Resources.Load("CADBs") as TextAsset;
        StringReader reader = new StringReader(csvFile.text);

        // , で分割しつつ一行ずつ読み込み
        // リストに追加していく
        while (reader.Peek() != -1) // reader.Peaekが-1になるまで
        {
            string line = reader.ReadLine(); // 一行ずつ読み込み
            csvDatas.Add(line.Split(',')); // , 区切りでリストに追加
        }

        // csvDatas[行][列]を指定して値を自由に取り出せる
        // Debug.Log(csvDatas[0][1]);

        loadedCADBs = new List<CADB>();
        foreach (string[] i in csvDatas) {
            CADB _data = new CADB();
            _data.number = int.Parse(i[0]);
            _data.name = i[1];
            _data.name_1 = i[2];
            _data.name_jp = i[3];
            _data.gender = i[4];
            _data.classname = i[5];
            _data.height = int.Parse(i[6]);
            _data.age = i[7];
            string _imagePath = "Images/" + i[8];
            _data.image = Resources.Load<Sprite>(_imagePath);
            loadedCADBs.Add(_data);
        }

    }

    CADB ReturnRandomCADB()
    {
        nowIndex = Mathf.FloorToInt(UnityEngine.Random.value * cADBs.Count);
        if (!cADBs[nowIndex].image) return ReturnRandomCADB();
        return cADBs[nowIndex];
    }

    CADB ReturnNextCADB()
    {
        nowIndex = (nowIndex + 1) % cADBs.Count;
        if (!cADBs[nowIndex].image) return ReturnNextCADB();
        return cADBs[nowIndex];
    }

    void SetNowDateTime()
    {
        nowDateTime = DateTime.Now;
        if (clockText)
            clockText.text = nowDateTime.Hour.ToString("D2") + " " + nowDateTime.Minute.ToString("D2");
    }

    void ChangeCA(bool isRandom)
    {
        nowCADB = isRandom ? ReturnRandomCADB() : ReturnNextCADB();
    }

    IEnumerator FadeOutCADB()
    {
        if (nowIndex < 0) yield break;
        while (alphaAnim > 0f)
        {
            numAnim = nowCADB.number + Mathf.FloorToInt(numAnimDuration * Mathf.Pow(1f - alphaAnim, acceralation));

            SetColorAlphaAnim();
            numText.text = (numAnim % 1000).ToString("D3");
            alphaAnim -= Time.deltaTime / animationTime;
            yield return null;
        }
        alphaAnim = 0f;
        SetColorAlphaAnim();
        numText.text = ((nowCADB.number + numAnimDuration) % 1000).ToString("D3");
    }

    IEnumerator AnimateCADB(bool isRandom)
    {
        yield return StartCoroutine(FadeOutCADB());
        ChangeCA(isRandom);
        SetOtherTextsFromCADB();
        while (alphaAnim < 1f)
        {
            numAnim = nowCADB.number - Mathf.FloorToInt(numAnimDuration * Mathf.Pow(1f - alphaAnim, acceralation));

            SetColorAlphaAnim();
            numText.text = ((numAnim + 1000) % 1000).ToString("D3");
            alphaAnim += Time.deltaTime / animationTime;
            yield return null;
        }
        alphaAnim = 1f;
        SetColorAlphaAnim();
        numText.text = nowCADB.number.ToString("D3");
    }

    void SetColorAlphaAnim()
    {
        characterImage.color = new Color(1f, 1f, 1f, alphaAnim);
        numText.color = nameText.color = name1Text.color = nameJpText.color
            = genderText.color = classnameText.color = heightText.color = ageText.color
            = new Color(textColor.r, textColor.g, textColor.b, alphaAnim);
    }

    void SetOtherTextsFromCADB()
    {
        characterImage.sprite = nowCADB.image;
        nameText.text = nowCADB.name;
        name1Text.text = nowCADB.name_1;
        nameJpText.text = nowCADB.name_jp;
        genderText.text = "Gender: " + nowCADB.gender;
        classnameText.text = "Class: " + nowCADB.classname;
        heightText.text = "Height: " + nowCADB.height.ToString() + "cm";
        ageText.text = "Consept Age: " + nowCADB.age;
    }
}
