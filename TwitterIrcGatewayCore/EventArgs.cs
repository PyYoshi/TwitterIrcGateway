using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.IO;
using Misuzilla.Net.Irc;

namespace Misuzilla.Applications.TwitterIrcGateway
{
    /// <summary>
    /// IRC���b�Z�[�W��M���C�x���g�̈���
    /// </summary>
    public class MessageReceivedEventArgs : CancelableEventArgs
    {
        /// <summary>
        /// ��M����IRC���b�Z�[�W���擾���܂�
        /// </summary>
        public IRCMessage Message { get; set; }
        /// <summary>
        /// �N���C�A���g�ւ̐ڑ����擾���܂�
        /// </summary>
        public TcpClient Client { get; private set; }
        /// <summary>
        /// �N���C�A���g�ւ̏o�͂̂��߂�StreamWriter���擾���܂�
        /// </summary>
        public StreamWriter Writer { get; private set; }

        public MessageReceivedEventArgs(IRCMessage msg, StreamWriter sw, TcpClient tcpClient)
        {
            Writer = sw;
            Client = tcpClient;
            Message = msg;
        }
    }

    /// <summary>
    /// �Z�b�V�������J�n���C�x���g�̈���
    /// </summary>
    public class SessionStartedEventArgs : EventArgs
    {
        public String UserName;
        public SessionStartedEventArgs(String userName)
        {
            UserName = userName;
        }
    }

    /// <summary>
    /// �L�����Z���\�ȃC�x���g�̈���
    /// </summary>
    public abstract class CancelableEventArgs : EventArgs
    {
        /// <summary>
        /// �������L�����Z�����邩�ǂ������擾�E�ݒ肵�܂�
        /// </summary>
        public Boolean Cancel { get; set; }
    }

    /// <summary>
    /// �^�C�����C���X�e�[�^�X�ꗗ���擾�����C�x���g�̈���
    /// </summary>
    public class TimelineStatusesEventArgs : CancelableEventArgs
    {
        /// <summary>
        /// �X�e�[�^�X�ꗗ���擾���܂�
        /// </summary>
        public Statuses Statuses { get; private set; }
        /// <summary>
        /// ����A�N�Z�X���ǂ������擾���܂�
        /// </summary>
        public Boolean IsFirstTime { get; set; }
        
        public TimelineStatusesEventArgs(Statuses statuses, Boolean isFirstTime)
        {
            Statuses = statuses;
            IsFirstTime = isFirstTime;
        }
    }
    
    /// <summary>
    /// �^�C�����C���X�e�[�^�X����������C�x���g�̈���
    /// </summary>
    public class TimelineStatusEventArgs : CancelableEventArgs
    {
        /// <summary>
        /// �󂯎�����X�e�[�^�X���擾���܂�
        /// </summary>
        public Status Status { get; private set; }
        /// <summary>
        /// ���ꂩ��N���C�A���g�ɑ��낤�Ƃ��Ă���{�����擾�E�ݒ肵�܂�
        /// </summary>
        public String Text { get; set; }
        /// <summary>
        /// �N���C�A���g�ɑ��M����IRC���b�Z�[�W�̎�ނ��擾�E�ݒ肵�܂�
        /// </summary>
        public String IRCMessageType { get; set; }
        
        public TimelineStatusEventArgs(Status status) : this(status, status.Text, "")
        {
        }
        public TimelineStatusEventArgs(Status status, String text, String ircMessageType)
        {
            Status = status;
            Text = text;
            IRCMessageType = ircMessageType;
        }
    }

    /// <summary>
    /// �X�e�[�^�X���N���C�A���g����X�V�����C�x���g�̈���
    /// </summary>
    public class StatusUpdateEventArgs : CancelableEventArgs
    {
        /// <summary>
        /// �N���C�A���g����󂯎����IRC���b�Z�[�W���擾���܂��B�^�C�~���O��Ăяo�����ɂ���Ă�null�ɂȂ�܂��B
        /// </summary>
        public PrivMsgMessage ReceivedMessage { get; set; }
        /// <summary>
        /// �X�V����̂ɗ��p����e�L�X�g���擾�E�ݒ肵�܂�
        /// </summary>
        public String Text { get; set; }
        /// <summary>
        /// �ԐM��̃X�e�[�^�X��ID���w�肵�܂��B0���w�肷��ƕԐM����w�肵�Ȃ��������ƂɂȂ�܂��B
        /// </summary>
        public Int32 InReplyToStatusId { get; set; }
        /// <summary>
        /// �X�e�[�^�X���X�V���Ă��̌��ʂ̃X�e�[�^�X���擾���܂��B�X�V�������̃C�x���g�ł̂ݗ��p�ł��܂��B
        /// </summary>
        public Status CreatedStatus { get; set; }

        public StatusUpdateEventArgs(String text, Int32 inReplyToStatusId)
        {
            Text = text;
            InReplyToStatusId = inReplyToStatusId;
        }
        
        public StatusUpdateEventArgs(PrivMsgMessage receivedMessage, String text)
        {
            ReceivedMessage = receivedMessage;
            Text = text;
        }
    }

    /// <summary>
    /// ���b�Z�[�W�̑��M������肵���C�x���g�̈���
    /// </summary>
    public class TimelineStatusRoutedEventArgs : EventArgs
    {
        /// <summary>
        /// �X�e�[�^�X���擾���܂�
        /// </summary>
        public Status Status { get; private set; }
        /// <summary>
        /// ���b�Z�[�W�̖{�����擾���܂�
        /// </summary>
        public String Text { get; private set; }
        /// <summary>
        /// ���肳�ꂽ���M��̃��X�g���擾���܂��B���̃��X�g�ɒǉ��܂��͍폜���邱�Ƃő��M���ύX�ł��܂��B
        /// </summary>
        public List<RoutedGroup> RoutedGroups { get; private set; }
        
        public TimelineStatusRoutedEventArgs(Status status, String text, List<RoutedGroup> routedGroups)
        {
            Status = status;
            Text = text;
            RoutedGroups = routedGroups;
        }
    }

    /// <summary>
    /// ���b�Z�[�W���e�O���[�v�ɑ��M����C�x���g�̈���
    /// </summary>
    public class TimelineStatusGroupEventArgs : TimelineStatusEventArgs
    {
        /// <summary>
        /// ���M�ΏۂƂȂ�O���[�v���擾���܂�
        /// </summary>
        public Group Group { get; private set; }

        public TimelineStatusGroupEventArgs(Status status, String text, String ircMessageType, Group group) : base(status, text, ircMessageType)
        {
            Group = group;
        }
    }
}
