# �X�e�[�^�X�X�V���O�̃C�x���g
Session.pre_send_update_status do |sender, e|
	# CLR String -> Ruby String �ɕϊ�����K�v��������ۂ�
	if e.text.to_s.include?("�͂��͂�")
		Session.send_server(Misuzilla::Net::Irc::NoticeMessage.new(e.received_message.receiver, "�͂��͂��ł𒥎�����!����܂ł͂͂��͂������Ȃ�!"))

		# �L�����Z�����邱�Ƃő��M�����Ȃ�
		e.cancel = true
	end
end

# �^�C�����C���̈�X�e�[�^�X����M���ăN���C�A���g�ɑ��M���钼�O�̃C�x���g
Session.pre_send_message_timeline_status do |sender, e|
	e.text = "#{e.text} (by #{e.status.user.name})"
end

# IRC���b�Z�[�W���󂯎�����Ƃ��̃C�x���g
Session.message_received do |sender, e|
	if e.message.command.to_s == "HAUHAU"
		Session.send_server_error_message("Hauhau!")
		e.cancel = true
	end
end