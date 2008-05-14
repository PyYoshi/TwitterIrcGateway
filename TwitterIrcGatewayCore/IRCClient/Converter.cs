// $Id$
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections;

namespace Misuzilla.Text.JapaneseStringUtilities
{
	public class Converter
	{
		private static readonly Hashtable _tableZenkakuToHankaku;
		private static readonly Char[] _tableHankakuToZenkakuHanDakuten;
		private static readonly Char[] _tableHankakuToZenkaku;
		private static readonly Char[,] _tableHankakuToZenkakuDakuten;

		private Converter() { }
		
		static Converter()
		{
			_tableZenkakuToHankaku = new Hashtable();
			_tableZenkakuToHankaku['�B'] = "�";
			_tableZenkakuToHankaku['�u'] = "�";
			_tableZenkakuToHankaku['�v'] = "�";
			_tableZenkakuToHankaku['�A'] = "�";
			_tableZenkakuToHankaku['�E'] = "�";
			_tableZenkakuToHankaku['��'] = "�";
			_tableZenkakuToHankaku['�@'] = "�";
			_tableZenkakuToHankaku['�B'] = "�";
			_tableZenkakuToHankaku['�D'] = "�";
			_tableZenkakuToHankaku['�F'] = "�";
			_tableZenkakuToHankaku['�H'] = "�";
			_tableZenkakuToHankaku['��'] = "�";
			_tableZenkakuToHankaku['��'] = "�";
			_tableZenkakuToHankaku['��'] = "�";
			_tableZenkakuToHankaku['�b'] = "�";
			_tableZenkakuToHankaku['�['] = "�";
			_tableZenkakuToHankaku['�A'] = "�";
			_tableZenkakuToHankaku['�C'] = "�";
			_tableZenkakuToHankaku['�E'] = "�";
			_tableZenkakuToHankaku['�G'] = "�";
			_tableZenkakuToHankaku['�I'] = "�";
			_tableZenkakuToHankaku['�J'] = "�";
			_tableZenkakuToHankaku['�L'] = "�";
			_tableZenkakuToHankaku['�N'] = "�";
			_tableZenkakuToHankaku['�P'] = "�";
			_tableZenkakuToHankaku['�R'] = "�";
			_tableZenkakuToHankaku['�T'] = "�";
			_tableZenkakuToHankaku['�V'] = "�";
			_tableZenkakuToHankaku['�X'] = "�";
			_tableZenkakuToHankaku['�Z'] = "�";
			_tableZenkakuToHankaku['�\'] = "�";
			_tableZenkakuToHankaku['�^'] = "�";
			_tableZenkakuToHankaku['�`'] = "�";
			_tableZenkakuToHankaku['�c'] = "�";
			_tableZenkakuToHankaku['�e'] = "�";
			_tableZenkakuToHankaku['�g'] = "�";
			_tableZenkakuToHankaku['�i'] = "�";
			_tableZenkakuToHankaku['�j'] = "�";
			_tableZenkakuToHankaku['�k'] = "�";
			_tableZenkakuToHankaku['�l'] = "�";
			_tableZenkakuToHankaku['�m'] = "�";
			_tableZenkakuToHankaku['�n'] = "�";
			_tableZenkakuToHankaku['�q'] = "�";
			_tableZenkakuToHankaku['�t'] = "�";
			_tableZenkakuToHankaku['�w'] = "�";
			_tableZenkakuToHankaku['�z'] = "�";
			_tableZenkakuToHankaku['�}'] = "�";
			_tableZenkakuToHankaku['�~'] = "�";
			_tableZenkakuToHankaku['��'] = "�";
			_tableZenkakuToHankaku['��'] = "�";
			_tableZenkakuToHankaku['��'] = "�";
			_tableZenkakuToHankaku['��'] = "�";
			_tableZenkakuToHankaku['��'] = "�";
			_tableZenkakuToHankaku['��'] = "�";
			_tableZenkakuToHankaku['��'] = "�";
			_tableZenkakuToHankaku['��'] = "�";
			_tableZenkakuToHankaku['��'] = "�";
			_tableZenkakuToHankaku['��'] = "��";
			_tableZenkakuToHankaku['�J'] = "�";
			_tableZenkakuToHankaku['�K'] = "�";
			_tableZenkakuToHankaku['�K'] = "��";
			_tableZenkakuToHankaku['�M'] = "��";
			_tableZenkakuToHankaku['�O'] = "��";
			_tableZenkakuToHankaku['�Q'] = "��";
			_tableZenkakuToHankaku['�S'] = "��";
			_tableZenkakuToHankaku['�U'] = "��";
			_tableZenkakuToHankaku['�W'] = "��";
			_tableZenkakuToHankaku['�Y'] = "��";
			_tableZenkakuToHankaku['�['] = "��";
			_tableZenkakuToHankaku['�]'] = "��";
			_tableZenkakuToHankaku['�_'] = "��";
			_tableZenkakuToHankaku['�a'] = "��";
			_tableZenkakuToHankaku['�d'] = "��";
			_tableZenkakuToHankaku['�f'] = "��";
			_tableZenkakuToHankaku['�h'] = "��";
			_tableZenkakuToHankaku['�o'] = "��";
			_tableZenkakuToHankaku['�r'] = "��";
			_tableZenkakuToHankaku['�u'] = "��";
			_tableZenkakuToHankaku['�x'] = "��";
			_tableZenkakuToHankaku['�{'] = "��";
			_tableZenkakuToHankaku['�p'] = "��";
			_tableZenkakuToHankaku['�s'] = "��";
			_tableZenkakuToHankaku['�v'] = "��";
			_tableZenkakuToHankaku['�y'] = "��";
			_tableZenkakuToHankaku['�|'] = "��";
			
			_tableHankakuToZenkaku = new Char[] {
				'�B', '�u', '�v', '�A', '�E', '��',
				'�@', '�B', '�D', '�F', '�H',
				'��', '��', '��', '�b', '�[',
				'�A', '�C', '�E', '�G', '�I',
				'�J', '�L', '�N', '�P', '�R',
				'�T', '�V', '�X', '�Z', '�\',
				'�^', '�`', '�c', '�e', '�g',
				'�i', '�j', '�k', '�l', '�m',
				'�n', '�q', '�t', '�w', '�z',
				'�}', '�~', '��', '��', '��',
				'��', '��', '��',
				'��', '��', '��', '��', '��',
				'��', '��',
				'�J', '�K',
			};
			_tableHankakuToZenkakuDakuten = new Char[,] {
				{' ', ' ', ' ', ' ', ' '},
				{'�K', '�M', '�O', '�Q', '�S'},
				{'�U', '�W', '�Y', '�[', '�]'},
				{'�_', '�a', '�d', '�f', '�h'},
				{' ', ' ', ' ', ' ', ' '},
				{'�o', '�r', '�u', '�x', '�{'},
			};
			_tableHankakuToZenkakuHanDakuten = new Char[] {
				'�p', '�s', '�v', '�y', '�|',
			};
		}
		
		public static String
		Convert(String str, ConvertFlags wideFlag, ConvertFlags narrowFlag)
		{
			StringBuilder sb = new StringBuilder();
			//Console.WriteLine("Convert In: {0}", str);
			for (Int32 i = 0; i < str.Length; i++) {
				Char c = str[i];
				Boolean isNextDakuten = (str.Length > i+1 ? (str[i+1] == '�') : false);
				Boolean isNextHanDakuten = (str.Length > i+1 ? (str[i+1] == '�') : false);
				//Console.WriteLine("  - char: {0}", c);
				//Console.WriteLine("  - isNextDakuten: {0}", isNextDakuten);
				//Console.WriteLine("  - isNextHanDakuten: {0}", isNextHanDakuten);
				
				if (((narrowFlag & ConvertFlags.Katakana) != 0) && _tableZenkakuToHankaku.ContainsKey(c)) {
					// �S�p�J�i -> ���p�J�i
					sb.Append(_tableZenkakuToHankaku[c]);
				} else if (((wideFlag & ConvertFlags.Katakana) != 0) && (c >= '�' && c <= '�')) {
					// ���p�J�i -> �S�p�J�i
					Int32 col = (c - '�') / 5; // �A�J�T�^�i�s
					Int32 row = (c - '�') % 5; // �A�C�E�G�I
					//Console.WriteLine("    - char: {0} at {1} - {2}", c, col, row);
					
					if (isNextDakuten) {
						switch (col) {
						case 1: case 2: case 3: case 5:
							//Console.WriteLine("      -> {0}", _tableHankakuToZenkakuDakuten[col, row]);
							sb.Append(_tableHankakuToZenkakuDakuten[col, row]);
							i++;
							break;
						default:
							if (c == '�') {
								sb.Append('��');
								i++;
							}
							break;
						}
					} else if (isNextHanDakuten && col == 5) {
						sb.Append(_tableHankakuToZenkakuHanDakuten[row]);
						i++;
					} else {
						//Console.WriteLine("      -> {0}", _tableHankakuToZenkaku[(c - '�')]);
						sb.Append(_tableHankakuToZenkaku[(c - '�')]);
					}
				} else if (((wideFlag & ConvertFlags.Alphabet) != 0) && (c >= '!' && c <= '~' && (c < '0' || c > '9'))) {
					// ���p�A���t�@�x�b�g -> �S�p�A���t�@�x�b�g
					sb.Append((Char)('�I' + (c - '!')));
				} else if (((narrowFlag & ConvertFlags.Alphabet) != 0) && (c >= '�I' && c <= '�`' && (c < '�O' || c > '�X'))) {
					// �S�p�A���t�@�x�b�g -> ���p�A���t�@�x�b�g
					sb.Append((Char)('!' + (c - '�I')));
				} else if (((wideFlag & ConvertFlags.Numeric) != 0) && (c >= '0' && c <= '9')) {
					// ���p���� -> �S�p����
					sb.Append((Char)('�O' + (c - '0')));
				} else if (((narrowFlag & ConvertFlags.Numeric) != 0) && (c >= '�O' && c <= '�X')) {
					// �S�p���� -> ���p����
					sb.Append((Char)('0' + (c - '�O')));
				} else if (((wideFlag & ConvertFlags.Space) != 0) && (c == ' ')) {
					// ���p�� -> �S�p��
					sb.Append('�@');
				} else if (((narrowFlag & ConvertFlags.Space) != 0) && (c == '�@')) {
					// �S�p�� -> ���p��
					sb.Append(' ');
				} else {
					sb.Append(c);
				}
			}
			return sb.ToString();
		}
	}
	[Flags]
	public enum ConvertFlags
	{
		None         = 0x0000,
		Katakana     = 0x0001,
		Numeric      = 0x0002,
		Alphabet     = 0x0004,
		AlphaNumeric = Numeric | Alphabet,
		Space        = 0x0008,
		All          = Katakana | AlphaNumeric | Space
	}
}